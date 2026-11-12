using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты шага <c>merchant-default</c> общего конвейера (T-03, ADR-032 D1; FR-M4/M8):
/// порядок existing → merchant-default → cache → precategorized → ai, ровно один batch-запрос
/// дефолтов на порцию, отказоустойчивая деградация, read-only. Провайдер замокан (NSubstitute);
/// EF-часть — <c>MerchantDefaultCategoryProviderTests</c>. Очередь и ИИ по очищенному ключу —
/// ADR-034 FR-2/FR-3 (T-05).
/// </summary>
public class CommodityCategoryResolverTests
{
    private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository = Substitute.For<IPrecategorizedCommodityRepository>();
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();
    private readonly IMerchantDefaultCategoryProvider _merchantDefaultProvider = Substitute.For<IMerchantDefaultCategoryProvider>();

    /// <summary>
    /// Кэш слов (ADR-035, T-05): по умолчанию пустой снимок — подсказок нет. Тесты подсказок
    /// подменяют снимок, провайдер остаётся реальным (матчинг проверяется сквозняком через resolver).
    /// </summary>
    private readonly ICommodityCategoryKeywordCache _keywordCache = Substitute.For<ICommodityCategoryKeywordCache>();

    public CommodityCategoryResolverTests()
    {
        // Фоновые шаги пусты по умолчанию — сценарии, сфокусированные на шаге merchant-default,
        // не меняются от посторонних попаданий (прецедент CommodityCategorizationServiceTests).
        _precategorizedRepository
            .GetCategoriesByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal));
        _merchantDefaultProvider
            .GetDefaultsByReceiptIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CommodityCategory?>());
        _keywordCache.GetSnapshot().Returns(CommodityCategoryKeywordSnapshot.Empty);
    }

    private CommodityCategoryResolver CreateResolver(ILogger<CommodityCategoryResolver>? logger = null) =>
        new(
            _cache,
            _precategorizedRepository,
            _aiClient,
            new CommodityCategoryHintProvider(_keywordCache),
            Options.Create(new AiOptions { BaseUrl = "http://localhost", Concurrency = 3 }),
            logger ?? NullLogger<CommodityCategoryResolver>.Instance,
            _merchantDefaultProvider);

    private void ArrangeCacheHit(string normalized, CommodityCategory category)
    {
        _cache.TryGet(normalized, out Arg.Any<CommodityCategory>())
            .Returns(callInfo =>
            {
                callInfo[1] = category;
                return true;
            });
    }

    private static Commodity CreateCommodity(
        Guid receiptId,
        string name,
        CommodityCategory? category = null) =>
        new(
            Guid.NewGuid(),
            receiptId,
            name,
            1m,
            10m,
            0,
            0m,
            category is { } value ? new Category((int)value, CommodityCategoryHelper.GetDisplayName(value)) : null);

    private void ArrangeMerchantDefaults(IReadOnlyDictionary<Guid, CommodityCategory?> defaults) =>
        _merchantDefaultProvider
            .GetDefaultsByReceiptIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(defaults);

    /// <summary>
    /// Подменяет снимок слов (ADR-035, T-05): провайдер в фабрике реальный, поэтому снимок —
    /// единственная точка управления подсказками в этих тестах.
    /// </summary>
    private void ArrangeKeywordSnapshot(params CommodityCategoryKeyword[] keywords) =>
        _keywordCache.GetSnapshot().Returns(new CommodityCategoryKeywordSnapshot(keywords));

    // ---------------- Приоритет шагов ----------------

    [Fact]
    public async Task Resolve_never_calls_merchant_default_provider_when_all_items_have_existing_category()
    {
        var receiptId = Guid.NewGuid();
        var item = CreateCommodity(receiptId, "Молоко", CommodityCategory.Dairy);

        var results = await CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Existing, result.Source);

        // Позиций без категории нет — batch за дефолтами не выполняется вовсе.
        await _merchantDefaultProvider
            .DidNotReceiveWithAnyArgs()
            .GetDefaultsByReceiptIdsAsync(default!, default);
    }

    [Fact]
    public async Task Resolve_prefers_existing_category_over_merchant_default()
    {
        // D3 отклонено: existing (в т.ч. ранее проставленная тем же дефолтом) всегда выше.
        var receiptId = Guid.NewGuid();
        var withExisting = CreateCommodity(receiptId, "Обед", CommodityCategory.Electronics);
        var withoutCategory = CreateCommodity(receiptId, "Кофе");
        ArrangeMerchantDefaults(new Dictionary<Guid, CommodityCategory?> { [receiptId] = CommodityCategory.ReadyMeals });

        var results = await CreateResolver()
            .ResolveAsync([withExisting, withoutCategory], aiEnabled: true, CancellationToken.None, CancellationToken.None);
        var items = results.ToList();

        var existing = items.Single(r => r.CommodityId == withExisting.Id);
        Assert.Equal(CategorizationSourceNames.Existing, existing.Source);
        Assert.Equal((int)CommodityCategory.Electronics, existing.CategoryId);

        var merchantDefault = items.Single(r => r.CommodityId == withoutCategory.Id);
        Assert.Equal(CategorizationSourceNames.MerchantDefault, merchantDefault.Source);
    }

    [Fact]
    public async Task Resolve_uses_merchant_default_and_skips_cache_queue_ai()
    {
        var receiptId = Guid.NewGuid();
        var item = CreateCommodity(receiptId, "Комплексный обед");
        ArrangeMerchantDefaults(new Dictionary<Guid, CommodityCategory?> { [receiptId] = CommodityCategory.ReadyMeals });

        // Наименование есть и в кэше, и в очереди — дефолт магазина приоритетнее их обоих.
        ArrangeCacheHit("комплексный обед", CommodityCategory.Food);
        _precategorizedRepository
            .GetCategoriesByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                ["Комплексный обед"] = CommodityCategory.Snacks
            });

        var results = await CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.MerchantDefault, result.Source);
        Assert.Equal((int)CommodityCategory.ReadyMeals, result.CategoryId);
        Assert.Equal(CommodityCategoryHelper.GetDisplayName(CommodityCategory.ReadyMeals), result.CategoryName);
        Assert.Null(result.Error);

        // FR-M4: для позиции с дефолтом кэш/очередь/ИИ не вызываются.
        _cache.DidNotReceiveWithAnyArgs().TryGet(default!, out _);
        await _precategorizedRepository
            .DidNotReceiveWithAnyArgs()
            .GetCategoriesByNormalizedNamesAsync(default!, default);
        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Resolve_merchant_without_default_falls_through_to_cache()
    {
        // FR-M8: магазин без дефолта (пустой словарь) — прежний поток без изменений.
        var receiptId = Guid.NewGuid();
        var item = CreateCommodity(receiptId, "АИ-95-К5");
        ArrangeCacheHit("аи-95-к5", CommodityCategory.Fuel);

        var results = await CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Cache, result.Source);
        Assert.Equal((int)CommodityCategory.Fuel, result.CategoryId);
    }

    [Fact]
    public async Task Resolve_treats_null_default_value_as_miss()
    {
        // Провайдер вернул явный null («магазина нет дефолта / невалидный код») — промах шага, дальше кэш.
        var receiptId = Guid.NewGuid();
        var item = CreateCommodity(receiptId, "Хлеб");
        ArrangeMerchantDefaults(new Dictionary<Guid, CommodityCategory?> { [receiptId] = null });
        ArrangeCacheHit("хлеб", CommodityCategory.Bakery);

        var results = await CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Cache, result.Source);
        Assert.Equal((int)CommodityCategory.Bakery, result.CategoryId);
    }

    [Fact]
    public async Task Resolve_mixed_batch_of_two_receipts_resolves_both_merchants()
    {
        // Одна порция страницы «Товары» из чеков двух магазинов: с дефолтом и без — смешанный результат.
        var defaultReceiptId = Guid.NewGuid();
        var plainReceiptId = Guid.NewGuid();
        var cafeteriaItem = CreateCommodity(defaultReceiptId, "Борщ");
        var groceryItem = CreateCommodity(plainReceiptId, "Молоко");

        ArrangeMerchantDefaults(new Dictionary<Guid, CommodityCategory?> { [defaultReceiptId] = CommodityCategory.ReadyMeals });
        ArrangeCacheHit("молоко", CommodityCategory.Dairy);

        var results = await CreateResolver()
            .ResolveAsync([cafeteriaItem, groceryItem], aiEnabled: true, CancellationToken.None, CancellationToken.None);
        var items = results.ToList();

        var cafeteria = items.Single(r => r.CommodityId == cafeteriaItem.Id);
        Assert.Equal(CategorizationSourceNames.MerchantDefault, cafeteria.Source);
        Assert.Equal((int)CommodityCategory.ReadyMeals, cafeteria.CategoryId);

        var grocery = items.Single(r => r.CommodityId == groceryItem.Id);
        Assert.Equal(CategorizationSourceNames.Cache, grocery.Source);
        Assert.Equal((int)CommodityCategory.Dairy, grocery.CategoryId);

        // Кэш опрашивается только для позиции магазина без дефолта.
        _cache.Received(1).TryGet("молоко", out _);
        _cache.DidNotReceive().TryGet("борщ", out _);
    }

    [Fact]
    public async Task Resolve_queries_merchant_defaults_once_with_distinct_receipt_ids()
    {
        // N+1 запрещён (стиль FR-9.3 ADR-027): один batch-запрос на порцию по различным receiptId,
        // позиции без категории одного чека не дают повторных запросов.
        var receiptA = Guid.NewGuid();
        var receiptB = Guid.NewGuid();
        var items = new[]
        {
            CreateCommodity(receiptA, "Позиция 1"),
            CreateCommodity(receiptA, "Позиция 2"),
            CreateCommodity(receiptB, "Позиция 3"),
            CreateCommodity(receiptA, "С категорией", CommodityCategory.Food)
        };

        var results = await CreateResolver().ResolveAsync(items, aiEnabled: false, CancellationToken.None, CancellationToken.None);

        await _merchantDefaultProvider.Received(1).GetDefaultsByReceiptIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(receiptA) && ids.Contains(receiptB)),
            Arg.Any<CancellationToken>());

        // ИИ выключен, промахи шагов — undefined с прежним текстом (поведение без дефолта не изменилось);
        // позиция с уже сохранённой категорией обслуживается шагом existing и в batch не попадает.
        var itemResults = results.ToList();
        Assert.Equal(CategorizationSourceNames.Existing, itemResults.Single(r => r.Name == "С категорией").Source);
        Assert.All(
            itemResults.Where(r => r.Name != "С категорией"),
            r => Assert.Equal(CategorizationSourceNames.Undefined, r.Source));
    }

    // ---------------- Деградация и отмена ----------------

    [Fact]
    public async Task Resolve_degrades_to_next_steps_with_warning_when_provider_fails()
    {
        // Ошибка batch-чтения дефолтов → Warning + «пустой» словарь (не 500): все идут дальше
        // по потоку — стиль GetQueueCategoriesAsync (FR-9.6, ADR-032 п. 5).
        var receiptId = Guid.NewGuid();
        var cachedItem = CreateCommodity(receiptId, "Кефир");
        var aiItem = CreateCommodity(Guid.NewGuid(), "Пельмени");
        ArrangeCacheHit("кефир", CommodityCategory.Dairy);
        // ADR-034 FR-2: в ИИ уходит очищенное имя позиции.
        _aiClient.SuggestCategoryAsync("пельмени", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.ReadyMeals));
        _merchantDefaultProvider
            .GetDefaultsByReceiptIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<Guid, CommodityCategory?>>(_ => throw new InvalidOperationException("db is down"));

        var logger = new CapturingLogger();
        var results = await CreateResolver(logger).ResolveAsync([cachedItem, aiItem], aiEnabled: true, CancellationToken.None, CancellationToken.None);
        var items = results.ToList();

        var kefir = items.Single(r => r.CommodityId == cachedItem.Id);
        Assert.Equal(CategorizationSourceNames.Cache, kefir.Source);
        var pelmeni = items.Single(r => r.CommodityId == aiItem.Id);
        Assert.Equal(CategorizationSourceNames.Ai, pelmeni.Source);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("merchant default", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_propagates_cancellation_from_merchant_default_provider()
    {
        var item = CreateCommodity(Guid.NewGuid(), "Товар");
        _merchantDefaultProvider
            .GetDefaultsByReceiptIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<Guid, CommodityCategory?>>(_ => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None));
    }

    // ---------------- Read-only и обратная совместимость ----------------

    [Fact]
    public async Task Resolve_merchant_default_step_does_not_write_to_cache()
    {
        // Suggest read-only (ADR-032 п. 5, решение F): предложение по дефолту никуда не сохраняется,
        // кэш пополняется только операциями применения (импорт/дожим, T-04).
        var receiptId = Guid.NewGuid();
        var item = CreateCommodity(receiptId, "Салат");
        ArrangeMerchantDefaults(new Dictionary<Guid, CommodityCategory?> { [receiptId] = CommodityCategory.Vegetables });

        await CreateResolver().ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task Resolve_without_provider_keeps_previous_pipeline_behaviour()
    {
        // Старый конструктор (без провайдера) — шаг пропускается, поведение конвейера прежнее:
        // существующие фабрики тестов компилируются и проходят без правок.
        var item = CreateCommodity(Guid.NewGuid(), "Сок");
        ArrangeCacheHit("сок", CommodityCategory.Beverages);

        var resolver = new CommodityCategoryResolver(
            _cache,
            _precategorizedRepository,
            _aiClient,
            new CommodityCategoryHintProvider(_keywordCache),
            Options.Create(new AiOptions { BaseUrl = "http://localhost", Concurrency = 3 }),
            NullLogger<CommodityCategoryResolver>.Instance);

        var results = await resolver.ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Cache, result.Source);
    }

    // ---------------- Очередь и ИИ по очищенному ключу (ADR-034 FR-2/FR-3) ----------------

    [Fact]
    public async Task Resolve_finds_queue_row_by_cleaned_key_and_skips_ai()
    {
        // FR-3 (ADR-034 п. 4.1): позиция с артикулом и EAN находит строку очереди,
        // сохранённую под очищенным ключом без кодов; ИИ не вызывается.
        var item = CreateCommodity(Guid.NewGuid(), "2195738 Туалетная бумага \"СВАЁ\" (3сл, 4р) 4810981001530");
        var cleanedKey = CommodityNameNormalizer.NormalizeName(item.Name); // туалетная бумага "сваё" (3сл, 4р)
        _precategorizedRepository
            .GetCategoriesByNormalizedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                [cleanedKey] = CommodityCategory.HouseholdChemicals
            });

        var results = await CreateResolver()
            .ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Precategorized, result.Source);
        Assert.Equal((int)CommodityCategory.HouseholdChemicals, result.CategoryId);
        // NFR-2: в ответе наименование остаётся сырым.
        Assert.Equal(item.Name, result.Name);

        // Batch-запрос в очередь передаёт очищенный ключ, а не сырое наименование.
        await _precategorizedRepository.Received(1).GetCategoriesByNormalizedNamesAsync(
            Arg.Is<IReadOnlyCollection<string>>(keys => keys.Count == 1 && keys.Contains(cleanedKey)),
            Arg.Any<CancellationToken>());
        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Resolve_sends_cleaned_name_to_ai()
    {
        // FR-2 (ADR-034 п. 4.1): в IAiClient уходит очищенное имя группы, а не сырое наименование.
        var item = CreateCommodity(Guid.NewGuid(), "2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ");
        _aiClient
            .SuggestCategoryAsync("пирог с вишней по ре", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Bakery));

        var results = await CreateResolver()
            .ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Ai, result.Source);
        Assert.Equal((int)CommodityCategory.Bakery, result.CategoryId);
        // NFR-2: в ответе наименование остаётся сырым.
        Assert.Equal(item.Name, result.Name);

        await _aiClient.Received(1)
            .SuggestCategoryAsync("пирог с вишней по ре", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_deduplicates_variants_with_and_without_code_into_one_ai_call()
    {
        // FR-3: варианты одного товара «с кодом» и «без кода» — один вызов ИИ по общему
        // очищенному ключу, обе позиции получают категорию из этого результата.
        var withCode = CreateCommodity(Guid.NewGuid(), "2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ");
        var plain = CreateCommodity(Guid.NewGuid(), "ПИРОГ С ВИШНЕЙ ПО РЕ");
        _aiClient
            .SuggestCategoryAsync("пирог с вишней по ре", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Bakery));

        var results = await CreateResolver()
            .ResolveAsync([withCode, plain], aiEnabled: true, CancellationToken.None, CancellationToken.None);
        var items = results.ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, r =>
        {
            Assert.Equal(CategorizationSourceNames.Ai, r.Source);
            Assert.Equal((int)CommodityCategory.Bakery, r.CategoryId);
        });
        // NFR-2: каждая позиция сохраняет своё сырое имя в ответе.
        Assert.Equal("2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ", items.Single(r => r.CommodityId == withCode.Id).Name);
        Assert.Equal("ПИРОГ С ВИШНЕЙ ПО РЕ", items.Single(r => r.CommodityId == plain.Id).Name);

        await _aiClient.Received(1)
            .SuggestCategoryAsync("пирог с вишней по ре", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>());
    }

    // ---------------- Подсказки по ключевым словам (ADR-035, T-05) ----------------

    [Fact]
    public async Task Resolve_sends_hint_collected_by_normalized_name_to_ai()
    {
        // ADR-035 п. 4.3: подсказка из снимка (DriedFruits/boost/изюм) уходит в IAiClient,
        // матчинг — по нормализованному имени («изюм 250 г»), а не по сырому наименованию.
        ArrangeKeywordSnapshot(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "Изюм", "изюм"));
        var item = CreateCommodity(Guid.NewGuid(), "Изюм 250 г");
        _aiClient
            .SuggestCategoryAsync("изюм 250 г", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.DriedFruits));

        var results = await CreateResolver()
            .ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        Assert.Equal(CategorizationSourceNames.Ai, Assert.Single(results).Source);
        await _aiClient.Received(1).SuggestCategoryAsync(
            "изюм 250 г",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Is<IReadOnlyCollection<CommodityCategoryKeywordHint>>(hints =>
                hints.SequenceEqual(new[] { new CommodityCategoryKeywordHint(CommodityCategory.DriedFruits, KeywordListType.Boost, "Изюм") })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_sends_empty_hints_collection_when_nothing_matches()
    {
        // ADR-035 D1: при отсутствии совпадений в IAiClient уходит пустая коллекция, а не null
        // (пустая = «совпадений нет», промт не меняется).
        var item = CreateCommodity(Guid.NewGuid(), "Молоко");
        _aiClient
            .SuggestCategoryAsync("молоко", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Dairy));

        await CreateResolver()
            .ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        await _aiClient.Received(1).SuggestCategoryAsync(
            "молоко",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Is<IReadOnlyCollection<CommodityCategoryKeywordHint>>(hints => hints != null && hints.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_deduplicates_same_cleaned_name_into_one_ai_call_with_same_hints()
    {
        // ADR-034 FR-3 + ADR-035 T-05: две позиции с одним очищенным именем — один вызов ИИ
        // с одним и тем же набором подсказок (один расчёт GetHints на ключ группы).
        ArrangeKeywordSnapshot(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "изюм", "изюм"));
        var withCode = CreateCommodity(Guid.NewGuid(), "2417268000006 ИЗЮМ 250 Г");
        var plain = CreateCommodity(Guid.NewGuid(), "Изюм 250 г");
        _aiClient
            .SuggestCategoryAsync("изюм 250 г", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.DriedFruits));

        var results = await CreateResolver()
            .ResolveAsync([withCode, plain], aiEnabled: true, CancellationToken.None, CancellationToken.None);
        var items = results.ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, r => Assert.Equal(CategorizationSourceNames.Ai, r.Source));
        await _aiClient.Received(1).SuggestCategoryAsync(
            "изюм 250 г",
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Is<IReadOnlyCollection<CommodityCategoryKeywordHint>>(hints =>
                hints.Count == 1 && hints.Single().Word == "изюм"),
            Arg.Any<CancellationToken>());
    }

    // ---------------- Wire-имена источников ----------------

    [Theory]
    [InlineData(CategorizationSource.Existing, "existing")]
    [InlineData(CategorizationSource.MerchantDefault, "merchant-default")]
    [InlineData(CategorizationSource.Cache, "cache")]
    [InlineData(CategorizationSource.Precategorized, "precategorized")]
    [InlineData(CategorizationSource.Ai, "ai")]
    [InlineData(CategorizationSource.Undefined, "undefined")]
    public void ToWireName_maps_all_sources_with_merchant_default_added(CategorizationSource source, string expected)
    {
        // ADR-032 п. 2: аддитивное "merchant-default"; существующие wire-имена не изменены.
        Assert.Equal(expected, CategorizationSourceNames.ToWireName(source));
    }

    /// <summary>Собирает сообщения Warning — по ним проверяется деградация без исключения.</summary>
    private sealed class CapturingLogger : ILogger<CommodityCategoryResolver>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
