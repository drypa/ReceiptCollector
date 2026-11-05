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
/// EF-часть — <c>MerchantDefaultCategoryProviderTests</c>.
/// </summary>
public class CommodityCategoryResolverTests
{
    private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository = Substitute.For<IPrecategorizedCommodityRepository>();
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();
    private readonly IMerchantDefaultCategoryProvider _merchantDefaultProvider = Substitute.For<IMerchantDefaultCategoryProvider>();

    public CommodityCategoryResolverTests()
    {
        // Фоновые шаги пусты по умолчанию — сценарии, сфокусированные на шаге merchant-default,
        // не меняются от посторонних попаданий (прецедент CommodityCategorizationServiceTests).
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal));
        _merchantDefaultProvider
            .GetDefaultsByReceiptIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CommodityCategory?>());
    }

    private CommodityCategoryResolver CreateResolver(ILogger<CommodityCategoryResolver>? logger = null) =>
        new(
            _cache,
            _precategorizedRepository,
            _aiClient,
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
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
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
            .GetCategoriesByExactNamesAsync(default!, default);
        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
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
        _aiClient.SuggestCategoryAsync("Пельмени", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
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
            Options.Create(new AiOptions { BaseUrl = "http://localhost", Concurrency = 3 }),
            NullLogger<CommodityCategoryResolver>.Instance);

        var results = await resolver.ResolveAsync([item], aiEnabled: true, CancellationToken.None, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CategorizationSourceNames.Cache, result.Source);
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
