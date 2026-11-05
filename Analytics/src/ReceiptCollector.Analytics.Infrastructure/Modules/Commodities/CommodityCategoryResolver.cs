using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Общий конвейер категоризации (ADR-028 п. 9): перенос шагов 1–4 из
/// <see cref="CommodityCategorizationService"/> **без изменения порядка и семантики**
/// + шаг <c>merchant-default</c> между <c>existing</c> и <c>cache</c> (ADR-032, D1; FR-M4).
/// <list type="number">
///   <item>категория уже присвоена товару (<c>existing</c>);</item>
///   <item>товарная категория по умолчанию магазина чека (<c>merchant-default</c>, ADR-032);</item>
///   <item>in-memory кэш (<c>cache</c>, ADR-019);</item>
///   <item>очередь предкатегорий одним batch-запросом (<c>precategorized</c>, ADR-027);</item>
///   <item>ИИ с дедупликацией по нормализованному имени (<c>ai</c> / <c>undefined</c>).</item>
/// </list>
/// Регистрация — Scoped: <see cref="SemaphoreSlim"/> создаётся на экземпляр, как раньше на
/// scoped-сервисе (публичное поведение не меняется, ADR-028 п. 12).
/// Suggest остаётся read-only: дефолт магазина только предлагается (<c>source = "merchant-default"</c>),
/// ничего не сохраняется и не кладётся в кэш (ADR-032 п. 5, решение F — запись только в операциях
/// импорта/дожима).
/// </summary>
internal sealed class CommodityCategoryResolver : ICommodityCategoryResolver
{
    /// <summary>Пустой результат поиска по очереди (деградация/нет наименований).</summary>
    private static readonly IReadOnlyDictionary<string, CommodityCategory> EmptyQueueCategories =
        new Dictionary<string, CommodityCategory>(StringComparer.Ordinal);

    /// <summary>Пустой результат batch-чтения дефолтов магазинов (деградация/нет позиций).</summary>
    private static readonly IReadOnlyDictionary<Guid, CommodityCategory?> EmptyMerchantDefaults =
        new Dictionary<Guid, CommodityCategory?>();

    /// <summary>Реестр категорий для AI (имена enum без Undefined) — строится из кода, не хардкодится.</summary>
    private static readonly IReadOnlyCollection<string> CategoryNames = CommodityCategoryHelper.GetAll()
        .Where(c => c.Id != CommodityCategory.Undefined)
        .Select(c => c.Id.ToString())
        .ToList();

    private const string AiNotDefinedError = "AI категория не определена.";
    private const string AiNotConfiguredError = "ИИ не настроен.";
    private const string AiTimeoutError = "ИИ не ответил за отведённое время.";

    private readonly ICommodityCategoryCache _cache;
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository;
    private readonly IAiClient _aiClient;
    private readonly ILogger<CommodityCategoryResolver> _logger;
    private readonly SemaphoreSlim _aiSemaphore;

    /// <summary>
    /// Batch-провайдер дефолтов магазинов (ADR-032, D1). Параметр со значением по умолчанию
    /// (<c>null</c> — шаг пропускается) сохранён для совместимости существующих вызовов
    /// (тестовые фабрики конвейера правок не требуют); в production регистрируется всегда
    /// (<c>DependencyInjectionExtensions</c>), поведение без провайдера совпадает с прежним.
    /// </summary>
    private readonly IMerchantDefaultCategoryProvider? _merchantDefaultProvider;

    public CommodityCategoryResolver(
        ICommodityCategoryCache cache,
        IPrecategorizedCommodityRepository precategorizedRepository,
        IAiClient aiClient,
        IOptions<AiOptions> aiOptions,
        ILogger<CommodityCategoryResolver> logger,
        IMerchantDefaultCategoryProvider? merchantDefaultProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _precategorizedRepository = precategorizedRepository ?? throw new ArgumentNullException(nameof(precategorizedRepository));
        _aiClient = aiClient ?? throw new ArgumentNullException(nameof(aiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _merchantDefaultProvider = merchantDefaultProvider;

        var options = aiOptions?.Value ?? throw new ArgumentNullException(nameof(aiOptions));
        var concurrency = options.Concurrency > 0 ? options.Concurrency : 1;
        _aiSemaphore = new SemaphoreSlim(concurrency, concurrency);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReceiptItemCategorizationResult>> ResolveAsync(
        IReadOnlyCollection<Commodity> items,
        bool aiEnabled,
        CancellationToken aiCancellationToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var results = new List<ReceiptItemCategorizationResult>(items.Count);

        // Шаг 1: категория уже присвоена товару (документ чека / данные аппы).
        var pending = new List<Commodity>();
        foreach (var item in items)
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

        // Шаг 2 (ADR-032, D1; FR-M4): товарная категория по умолчанию магазина чека —
        // между existing и cache (ручная/сохранённая категория приоритетнее дефолта, D3 отклонено).
        // receiptIds собираются по позициям, оставшимся без категории после шага 1; порция
        // может относиться к разным магазинам — обрабатываются все, ровно один batch-запрос.
        var pendingAfterMerchantDefault = new List<Commodity>();
        if (pending.Count > 0 && _merchantDefaultProvider is not null)
        {
            var merchantDefaults = await GetMerchantDefaultCategoriesAsync(pending, cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in pending)
            {
                if (merchantDefaults.TryGetValue(item.ReceiptId, out var merchantDefault)
                    && merchantDefault is { } defaultCategory
                    && defaultCategory != CommodityCategory.Undefined
                    && Enum.IsDefined(typeof(CommodityCategory), defaultCategory))
                {
                    // Позиции с дефолтом: кэш/очередь/ИИ для них не вызываются (FR-M4);
                    // результат только предложение — resolver ничего не пишет (ADR-032 п. 5, F).
                    results.Add(CreateResult(
                        item,
                        (int)defaultCategory,
                        CommodityCategoryHelper.GetDisplayName(defaultCategory),
                        CategorizationSource.MerchantDefault,
                        null));
                }
                else
                {
                    // FR-M8: магазин без дефолта (null/нет ключа) — прежний поток без изменений.
                    pendingAfterMerchantDefault.Add(item);
                }
            }
        }
        else
        {
            pendingAfterMerchantDefault.AddRange(pending);
        }

        // Шаг 3: in-memory кэш ранее присвоенных категорий (сквозной, без userId; FR-2.4).
        var pendingAfterCache = new List<Commodity>();
        foreach (var item in pendingAfterMerchantDefault)
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

        // Шаг 4: очередь предкатегорий (ADR-027, D13/FR-9): один batch-запрос на порцию,
        // только для наименований, которым не нашлось категории на existing/merchant-default/cache (FR-9.3).
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

        // Шаг 5: AI. Одинаковые нормализованные названия категоризируем один раз.
        if (aiCandidates.Count > 0)
        {
            if (!aiEnabled)
            {
                // ADR-028 п. 4 (C7): ИИ не настроен — остаток «не определена», без исключения и 503.
                foreach (var item in aiCandidates)
                {
                    results.Add(CreateResult(item, null, null, CategorizationSource.Undefined, AiNotConfiguredError));
                }
            }
            else
            {
                var suggestions = await SuggestByAiAsync(aiCandidates, aiCancellationToken, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var item in aiCandidates)
                {
                    var key = CommodityNameNormalizer.NormalizeName(item.Name);
                    if (suggestions.TryGetValue(key, out var outcome) && outcome.Suggestion is { } suggestion)
                    {
                        var categoryName = CommodityCategoryHelper.GetDisplayName(suggestion.Category);
                        results.Add(CreateResult(item, (int)suggestion.Category, categoryName, CategorizationSource.Ai, null));
                    }
                    else
                    {
                        var error = outcome.Error ?? AiNotDefinedError;
                        results.Add(CreateResult(item, null, null, CategorizationSource.Undefined, error));
                    }
                }
            }
        }

        return results;
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

    /// <summary>
    /// Batch-чтение товарных категорий по умолчанию магазинов по receiptId позиций без категории
    /// (ADR-032, D1): ровно один запрос провайдера на порцию (N+1 запрещён — стиль FR-9.3 ADR-027).
    /// Ошибка чтения не ломает категоризацию: Warning + «пустой» словарь — все позиции идут дальше
    /// по потоку (cache → precategorized → ai), стиль <see cref="GetQueueCategoriesAsync"/> (FR-9.6);
    /// <see cref="OperationCanceledException"/> пробрасывается.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetMerchantDefaultCategoriesAsync(
        IReadOnlyCollection<Commodity> items,
        CancellationToken cancellationToken)
    {
        var receiptIds = items
            .Select(item => item.ReceiptId)
            .Distinct()
            .ToList();

        if (receiptIds.Count == 0 || _merchantDefaultProvider is null)
        {
            return EmptyMerchantDefaults;
        }

        try
        {
            var defaults = await _merchantDefaultProvider
                .GetDefaultsByReceiptIdsAsync(receiptIds, cancellationToken)
                .ConfigureAwait(false);

            return defaults ?? EmptyMerchantDefaults;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read merchant default commodity categories; falling back to cache/queue/AI categorization.");
            return EmptyMerchantDefaults;
        }
    }

    /// <summary>
    /// Параллельный вызов ИИ с ограничением concurrency; результат — словарь
    /// normalizedName → исход (категория либо текст ошибки по названию).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, AiCategorizationOutcome>> SuggestByAiAsync(
        IReadOnlyCollection<Commodity> items,
        CancellationToken aiCancellationToken,
        CancellationToken cancellationToken)
    {
        var uniqueItems = items
            .GroupBy(i => CommodityNameNormalizer.NormalizeName(i.Name))
            .Select(g => (Key: g.Key, Name: g.First().Name))
            .ToList();

        var tasks = uniqueItems.Select(async item =>
        {
            var acquired = false;
            try
            {
                await _aiSemaphore.WaitAsync(aiCancellationToken).ConfigureAwait(false);
                acquired = true;

                var result = await _aiClient
                    .SuggestCategoryAsync(item.Name, CategoryNames, aiCancellationToken)
                    .ConfigureAwait(false);

                return (item.Key, Outcome: new AiCategorizationOutcome(result, null));
            }
            catch (AiClientException ex)
            {
                _logger.LogWarning(ex, "AI categorization failed for '{ProductName}'.", item.Name);
                return (item.Key, Outcome: new AiCategorizationOutcome(null, AiNotDefinedError));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Дедлайн бюджета запроса (ADR-028 п. 3): только это название становится undefined,
                // остальные продолжают обрабатываться; ответ остаётся частичным (200).
                _logger.LogWarning("AI categorization deadline exceeded for '{ProductName}'.", item.Name);
                return (item.Key, Outcome: new AiCategorizationOutcome(null, AiTimeoutError));
            }
            finally
            {
                if (acquired)
                {
                    _aiSemaphore.Release();
                }
            }
        });

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);
        return completed.ToDictionary(t => t.Key, t => t.Outcome, StringComparer.Ordinal);
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

    /// <summary>Исход вызова ИИ по одному нормализованному названию.</summary>
    private readonly record struct AiCategorizationOutcome(AiSuggestionResult? Suggestion, string? Error);
}
