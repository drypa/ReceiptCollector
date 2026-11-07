using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Реализация сценария «Автоматическая категоризация товаров чека» (UC-5, ADR 009).
/// Приоритет: категория из чека → in-memory кэш категорий (ADR 019) → очередь предкатегорий
/// (ADR-027, D13/FR-9) → AI (решение C2). Сам конвейер вынесен в общий
/// <see cref="ICommodityCategoryResolver"/> (ADR-028 п. 9) — поведение suggest чека не меняется.
/// Suggest ничего не сохраняет; подтверждение пользователя применяется через
/// <see cref="ApplyConfirmedCategoriesAsync"/>. При подтверждении категория кладётся в сквозной
/// in-memory кэш (без userId — ADR 019, FR-2.4).
/// </summary>
internal sealed class CommodityCategorizationService : ICommodityCategorizationService
{
    private readonly IReceiptRepository _receiptRepository;
    private readonly ICommodityRepository _commodityRepository;
    private readonly ICommodityCategoryCache _cache;
    private readonly ICommodityCategoryResolver _resolver;
    private readonly IOptions<AiOptions> _aiOptions;
    private readonly ICategorizationDecisionRecorder _categoryDecisionRecorder;
    private readonly ILogger<CommodityCategorizationService> _logger;

    public CommodityCategorizationService(
        IReceiptRepository receiptRepository,
        ICommodityRepository commodityRepository,
        ICommodityCategoryCache cache,
        ICommodityCategoryResolver resolver,
        IOptions<AiOptions> aiOptions,
        ICategorizationDecisionRecorder categoryDecisionRecorder,
        ILogger<CommodityCategorizationService> logger)
    {
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _commodityRepository = commodityRepository ?? throw new ArgumentNullException(nameof(commodityRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
        _categoryDecisionRecorder = categoryDecisionRecorder ?? throw new ArgumentNullException(nameof(categoryDecisionRecorder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReceiptCategorizationResult> SuggestForReceiptAsync(
        Guid userId,
        Guid receiptId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await _receiptRepository.GetByIdAsync(receiptId, userId, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            throw new ReceiptNotFoundException(receiptId);
        }

        // Поведение suggest чека не меняется: здесь ИИ включается по наличию AI:BaseUrl
        // (при пустом BaseUrl эндпоинт чека отвечает 503 ещё до вызова сервиса), а дедлайн
        // бюджета страницы (ADR-028) к чеку не применяется — aiCancellationToken == cancellationToken.
        var aiEnabled = !string.IsNullOrWhiteSpace(_aiOptions.Value.BaseUrl);

        var results = await _resolver
            .ResolveAsync(receipt.Items, aiEnabled, cancellationToken, cancellationToken)
            .ConfigureAwait(false);

        return new ReceiptCategorizationResult(receiptId, results);
    }

    public async Task<int> ApplyConfirmedCategoriesAsync(
        Guid userId,
        Guid receiptId,
        IReadOnlyCollection<ConfirmedCategoryAssignment> items,
        bool acceptedByAdmin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var receipt = await _receiptRepository.GetByIdAsync(receiptId, userId, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            throw new ReceiptNotFoundException(receiptId);
        }

        foreach (var invalid in items.Where(i => i.Category == CommodityCategory.Undefined))
        {
            throw new ArgumentException(
                $"Commodity '{invalid.CommodityId}': category Undefined cannot be explicitly assigned.");
        }

        var commoditiesById = receipt.Items.ToDictionary(i => i.Id);
        var decisions = new List<CategorizationDecisionDraft>();

        var updated = 0;
        foreach (var assignment in items)
        {
            if (!commoditiesById.TryGetValue(assignment.CommodityId, out var commodity))
            {
                // Товар не принадлежит чеку — игнорируем (защита от массового обновления чужих товаров).
                // «Применённого» присвоения нет, значит и события журнала нет (ADR-033 п. 3.3).
                continue;
            }

            await _commodityRepository
                .UpdateCategoryAsync(assignment.CommodityId, assignment.Category, cancellationToken)
                .ConfigureAwait(false);

            if (assignment.Category is { } category)
            {
                var normalized = CommodityNameNormalizer.NormalizeName(commodity.Name);
                _cache.TryAdd(normalized, category); // first-wins (FR-1.4); no-op при лимите (FR-1.5)
            }

            // Черновик события — только по применённому присвоению с непустым снимком предложения
            // (ADR-033 п. 3, FR-5b). Сброс категории (null) даёт final = Undefined (edge case задачи).
            if (assignment.SuggestedCategory is { } suggestedCategory
                && !string.IsNullOrWhiteSpace(assignment.SuggestionSource))
            {
                decisions.Add(new CategorizationDecisionDraft(
                    commodity.Name,
                    assignment.SuggestionSource,
                    suggestedCategory,
                    assignment.Category ?? CommodityCategory.Undefined,
                    userId,
                    acceptedByAdmin));
            }

            updated++;
        }

        if (decisions.Count > 0)
        {
            // ADR-033 п. 3.5/C1: журнал пишется после commit всех пользовательских сохранений
            // (UpdateCategoryAsync коммитит каждое присвоение) и никогда не влияет на результат.
            await RecordDecisionsBestEffortAsync(decisions, cancellationToken).ConfigureAwait(false);
        }

        return updated;
    }

    /// <summary>
    /// Вызов журнала строго после сохранения (NFR-3). Recorder по контракту (T-03) не бросает;
    /// защитный <c>catch</c> гарантирует NFR-3 и в случае нарушения контракта. Запись журнала —
    /// последняя БД-операция запроса: после неё сервис и эндпоинт ничего не сохраняют, поэтому
    /// оставшиеся в change tracker сущности после сбоя <c>SaveChangesAsync</c> повторно не вставятся.
    /// </summary>
    private async Task RecordDecisionsBestEffortAsync(
        IReadOnlyCollection<CategorizationDecisionDraft> decisions,
        CancellationToken cancellationToken)
    {
        try
        {
            await _categoryDecisionRecorder.RecordAsync(decisions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Categorization decision log call failed after receipt categories were saved: {DecisionCount} events.",
                decisions.Count);
        }
    }
}
