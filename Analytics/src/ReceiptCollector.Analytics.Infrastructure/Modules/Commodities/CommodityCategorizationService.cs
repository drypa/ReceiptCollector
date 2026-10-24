using Microsoft.Extensions.Logging;
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
/// (ADR-027, D13/FR-9) → AI (решение C2). Suggest ничего не сохраняет; подтверждение
/// пользователя применяется через <see cref="ApplyConfirmedCategoriesAsync"/>. При подтверждении
/// категория кладётся в сквозной in-memory кэш (без userId — ADR 019, FR-2.4).
/// </summary>
internal sealed class CommodityCategorizationService : ICommodityCategorizationService
{
    private readonly IReceiptRepository _receiptRepository;
    private readonly ICommodityRepository _commodityRepository;
    private readonly ICommodityCategoryCache _cache;
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository;
    private readonly IAiClient _aiClient;
    private readonly ILogger<CommodityCategorizationService> _logger;
    private readonly SemaphoreSlim _aiSemaphore;

    /// <summary>Пустой результат поиска по очереди (деградация/нет наименований).</summary>
    private static readonly IReadOnlyDictionary<string, CommodityCategory> EmptyQueueCategories =
        new Dictionary<string, CommodityCategory>(StringComparer.Ordinal);

    /// <summary>Реестр категорий для AI (имена enum без Undefined) — строится из кода, не хардкодится.</summary>
    private static readonly IReadOnlyCollection<string> CategoryNames = CommodityCategoryHelper.GetAll()
        .Where(c => c.Id != CommodityCategory.Undefined)
        .Select(c => c.Id.ToString())
        .ToList();

    public CommodityCategorizationService(
        IReceiptRepository receiptRepository,
        ICommodityRepository commodityRepository,
        ICommodityCategoryCache cache,
        IPrecategorizedCommodityRepository precategorizedRepository,
        IAiClient aiClient,
        IOptions<AiOptions> aiOptions,
        ILogger<CommodityCategorizationService> logger)
    {
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _commodityRepository = commodityRepository ?? throw new ArgumentNullException(nameof(commodityRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _precategorizedRepository = precategorizedRepository ?? throw new ArgumentNullException(nameof(precategorizedRepository));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = aiOptions?.Value ?? throw new ArgumentNullException(nameof(aiOptions));
        var concurrency = options.Concurrency > 0 ? options.Concurrency : 1;
        _aiSemaphore = new SemaphoreSlim(concurrency, concurrency);
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

        var results = new List<ReceiptItemCategorizationResult>(receipt.Items.Count);

        // Шаг 1: категория уже присвоена товару (документ чека / данные аппы).
        var pending = new List<Commodity>();
        foreach (var item in receipt.Items)
        {
            if (item.Category is { Id: not 0 } existingCategory)
            {
                results.Add(CreateResult(item, existingCategory.Id, existingCategory.Name, CategorizationSource.Existing, null));
            }
            else
            {
                pending.Add(item);
            }
        }

        // Шаг 2: in-memory кэш ранее присвоенных категорий (сквозной, без userId; FR-2.4).
        var pendingAfterCache = new List<Commodity>();
        foreach (var item in pending)
        {
            var normalized = CommodityNameNormalizer.NormalizeName(item.Name);
            if (_cache.TryGet(normalized, out var category))
            {
                // Дисплейное имя — из CommodityCategoryHelper, а не из БД (ADR 019, «Как новая схема устраняет причины»).
                var categoryName = CommodityCategoryHelper.GetDisplayName(category);
                results.Add(CreateResult(item, (int)category, categoryName, CategorizationSource.Cache, null));
            }
            else
            {
                pendingAfterCache.Add(item);
            }
        }

        // Шаг 3: очередь предкатегорий (ADR-027, D13/FR-9): один batch-запрос на чек,
        // только для наименований, которым не нашлось категории на existing/cache (FR-9.3).
        var aiCandidates = new List<Commodity>();
        if (pendingAfterCache.Count > 0)
        {
            var queueCategories = await GetQueueCategoriesAsync(pendingAfterCache, cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in pendingAfterCache)
            {
                if (queueCategories.TryGetValue(item.Name, out var queuedCategory)
                    && queuedCategory != CommodityCategory.Undefined
                    && Enum.IsDefined(typeof(CommodityCategory), queuedCategory))
                {
                    // FR-9.4: источник precategorized, ИИ для позиции не вызывается.
                    results.Add(CreateResult(
                        item,
                        (int)queuedCategory,
                        CommodityCategoryHelper.GetDisplayName(queuedCategory),
                        CategorizationSource.Precategorized,
                        null));
                }
                else
                {
                    aiCandidates.Add(item);
                }
            }
        }

        // Шаг 4: AI. Одинаковые нормализованные названия категоризируем один раз.
        if (aiCandidates.Count > 0)
        {
            var suggestions = await SuggestByAiAsync(aiCandidates, cancellationToken).ConfigureAwait(false);

            foreach (var item in aiCandidates)
            {
                var key = CommodityNameNormalizer.NormalizeName(item.Name);
                if (suggestions.TryGetValue(key, out var suggestion))
                {
                    var categoryName = CommodityCategoryHelper.GetDisplayName(suggestion.Category);
                    results.Add(CreateResult(item, (int)suggestion.Category, categoryName, CategorizationSource.Ai, null));
                }
                else
                {
                    results.Add(CreateResult(item, null, null, CategorizationSource.Undefined, "AI категория не определена."));
                }
            }
        }

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

    /// <summary>
    /// Batch-поиск категорий в очереди предкатегорий по точным наименованиям (FR-9.2/9.3).
    /// Ошибка чтения очереди не ломает категоризацию: Warning + деградация к ИИ (FR-9.6).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, CommodityCategory>> GetQueueCategoriesAsync(
        IReadOnlyCollection<Commodity> items,
        CancellationToken cancellationToken)
    {
        var names = items
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return EmptyQueueCategories;
        }

        try
        {
            var categories = await _precategorizedRepository
                .GetCategoriesByExactNamesAsync(names, cancellationToken)
                .ConfigureAwait(false);

            return categories ?? EmptyQueueCategories;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read precategorized commodities; falling back to AI categorization.");
            return EmptyQueueCategories;
        }
    }

    /// <summary>Параллельный вызов AI с ограничением concurrency; возвращает словарь normalizedName → категория.</summary>
    private async Task<IReadOnlyDictionary<string, AiSuggestionResult>> SuggestByAiAsync(
        IReadOnlyCollection<Commodity> items,
        CancellationToken cancellationToken)
    {
        var uniqueItems = items
            .GroupBy(i => CommodityNameNormalizer.NormalizeName(i.Name))
            .Select(g => (Key: g.Key, Name: g.First().Name))
            .ToList();

        var tasks = uniqueItems.Select(async item =>
        {
            await _aiSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return (item.Key, Result: await _aiClient
                    .SuggestCategoryAsync(item.Name, CategoryNames, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (AiClientException ex)
            {
                _logger.LogWarning(ex, "AI categorization failed for '{ProductName}'.", item.Name);
                return (item.Key, Result: (AiSuggestionResult?)null);
            }
            finally
            {
                _aiSemaphore.Release();
            }
        });

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);
        return completed
            .Where(t => t.Result is not null)
            .ToDictionary(t => t.Key, t => t.Result!);
    }

    private static ReceiptItemCategorizationResult CreateResult(
        Commodity item,
        int? categoryId,
        string? categoryName,
        CategorizationSource source,
        string? error)
    {
        return new ReceiptItemCategorizationResult(
            item.Id,
            item.Name,
            categoryId,
            categoryName,
            CategorizationSourceNames.ToWireName(source),
            error);
    }
}