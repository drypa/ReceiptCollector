using Microsoft.Extensions.Options;
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

    public CommodityCategorizationService(
        IReceiptRepository receiptRepository,
        ICommodityRepository commodityRepository,
        ICommodityCategoryCache cache,
        ICommodityCategoryResolver resolver,
        IOptions<AiOptions> aiOptions)
    {
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _commodityRepository = commodityRepository ?? throw new ArgumentNullException(nameof(commodityRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
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

        var updated = 0;
        foreach (var assignment in items)
        {
            if (!commoditiesById.TryGetValue(assignment.CommodityId, out var commodity))
            {
                // Товар не принадлежит чеку — игнорируем (защита от массового обновления чужих товаров).
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

            updated++;
        }

        return updated;
    }
}
