using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class InMemoryCommodityCategoryCacheTests
{
    [Fact]
    public void TryAdd_then_TryGet_returns_stored_category()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);

        Assert.True(cache.TryAdd("молоко", CommodityCategory.Dairy));
        Assert.True(cache.TryGet("молоко", out var category));
        Assert.Equal(CommodityCategory.Dairy, category);
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_key()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);

        Assert.False(cache.TryGet("неизвестный", out _));
    }

    [Fact]
    public void TryAdd_is_first_wins_for_existing_key()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);

        Assert.True(cache.TryAdd("молоко", CommodityCategory.Dairy));
        var countAfterFirstAdd = cache.Count;

        Assert.False(cache.TryAdd("молоко", CommodityCategory.Food));
        Assert.True(cache.TryGet("молоко", out var category));
        Assert.Equal(CommodityCategory.Dairy, category);
        Assert.Equal(countAfterFirstAdd, cache.Count); // FR-1.4: значение не перезаписывается
    }

    [Fact]
    public void TryAdd_freezes_at_max_size_without_eviction()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 2);

        Assert.True(cache.TryAdd("к1", CommodityCategory.Food));
        Assert.True(cache.TryAdd("к2", CommodityCategory.Dairy));
        Assert.False(cache.TryAdd("к3", CommodityCategory.Fuel)); // лимит достигнут — no-op

        Assert.Equal(2, cache.Count); // ничего не вытеснено
        Assert.True(cache.TryGet("к1", out var c1) && c1 == CommodityCategory.Food);
        Assert.True(cache.TryGet("к2", out var c2) && c2 == CommodityCategory.Dairy);
        Assert.False(cache.TryGet("к3", out _));
    }

    [Fact]
    public void TryAdd_freezes_at_default_max_size_1000()
    {
        var defaultMaxSize = Options.Create(new CommodityCategoryCacheOptions()).Value.MaxSize;
        var cache = new InMemoryCommodityCategoryCache(defaultMaxSize);

        for (var i = 0; i < defaultMaxSize; i++)
        {
            Assert.True(cache.TryAdd($"key-{i}", CommodityCategory.Other));
        }

        Assert.Equal(1000, cache.Count);
        Assert.False(cache.TryAdd("key-overflow", CommodityCategory.Other)); // 1001-я запись — заморозка
        Assert.Equal(1000, cache.Count);
    }

    [Fact]
    public void TryAdd_rejects_undefined_category()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);

        Assert.False(cache.TryAdd("x", CommodityCategory.Undefined));
        Assert.Equal(0, cache.Count); // FR-1.2: Undefined в кэш не попадает
        Assert.False(cache.TryGet("x", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryAdd_disables_cache_when_max_size_below_one(int maxSize)
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize);

        Assert.False(cache.TryAdd("ключ", CommodityCategory.Food)); // кэш отключён
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("ключ", out _));
    }
}