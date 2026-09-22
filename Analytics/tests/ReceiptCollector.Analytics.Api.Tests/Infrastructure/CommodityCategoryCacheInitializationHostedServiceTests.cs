using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public class CommodityCategoryCacheInitializationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_populates_cache_from_commodities_with_category()
    {
        var (provider, cache) = CreateHost(maxSize: 100, seed: db =>
        {
            db.Commodities.AddRange(
                Commodity("Молоко", categoryId: (int)CommodityCategory.Dairy),
                Commodity("Без категории", categoryId: null),
                Commodity("Не указана", categoryId: 0),
                Commodity("Битый код", categoryId: 9999));
        });

        var hosted = CreateHostedService(provider, cache, maxSize: 100);
        await hosted.StartAsync(CancellationToken.None);

        // FR-1.2/FR-1.7: категория ≠ Undefined попадает; без категории / Undefined / невалидный код — нет.
        Assert.True(cache.TryGet("молоко", out var category));
        Assert.Equal(CommodityCategory.Dairy, category);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet("без категории", out _));
        Assert.False(cache.TryGet("не указана", out _));
        Assert.False(cache.TryGet("битый код", out _));
    }

    [Fact]
    public async Task StartAsync_normalizes_keys_and_first_wins_for_equal_names()
    {
        var (provider, cache) = CreateHost(maxSize: 100, seed: db =>
        {
            // «АИ-95-К5» и его вариант с хвостовым пробелом нормализуются в один ключ "аи-95-к5".
            // Хвостовой пробел (в отличие от лидирующего) даёт детерминированного победителя в обеих коллациях:
            // и InMemory (Comparer<string>.Default), и PostgreSQL (байтовый порядок) ставят строку без пробела
            // первой (проверено эмпирически), поэтому first-wins выигрывает Fuel. Для теста важен
            // детерминированный порядок, а не конкретная коллация (ADR 019 п.4).
            db.Commodities.AddRange(
                Commodity("АИ-95-К5", categoryId: (int)CommodityCategory.Fuel),
                Commodity("АИ-95-К5 ", categoryId: (int)CommodityCategory.Dairy));
        });

        var hosted = CreateHostedService(provider, cache, maxSize: 100);
        await hosted.StartAsync(CancellationToken.None);

        // Оба названия нормализуются в "аи-95-к5"; first-wins: первое в OrderBy(Name) значение выигрывает.
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("аи-95-к5", out var category));
        Assert.Equal(CommodityCategory.Fuel, category);
    }

    [Fact]
    public async Task StartAsync_stops_filling_when_limit_reached()
    {
        var (provider, cache) = CreateHost(maxSize: 2, seed: db =>
        {
            db.Commodities.AddRange(
                Commodity("Первый", categoryId: (int)CommodityCategory.Food),
                Commodity("Второй", categoryId: (int)CommodityCategory.Dairy),
                Commodity("Третий", categoryId: (int)CommodityCategory.Fuel),
                Commodity("Четвёртый", categoryId: (int)CommodityCategory.Other),
                Commodity("Пятый", categoryId: (int)CommodityCategory.Pharmacy));
        });

        var hosted = CreateHostedService(provider, cache, maxSize: 2);
        await hosted.StartAsync(CancellationToken.None);

        Assert.Equal(2, cache.Count); // ранний выход при Count == MaxSize
        Assert.False(cache.TryAdd("шестой", CommodityCategory.Clothing)); // «замерзание» (FR-1.5)
    }

    [Fact]
    public async Task StartAsync_does_nothing_when_cache_disabled()
    {
        var (provider, cache) = CreateHost(maxSize: 0, seed: db =>
        {
            db.Commodities.AddRange(Commodity("Молоко", categoryId: (int)CommodityCategory.Dairy));
        });

        var hosted = CreateHostedService(provider, cache, maxSize: 0);
        await hosted.StartAsync(CancellationToken.None); // без исключений

        Assert.Equal(0, cache.Count); // кэш отключён (инвариант 7)
        Assert.False(cache.TryGet("молоко", out _));
    }

    private static (ServiceProvider Provider, InMemoryCommodityCategoryCache Cache) CreateHost(
        int maxSize,
        Action<ReceiptDbContext> seed)
    {
        // Фиксированное имя InMemory-БД: провайдер и hosted-сервис читают одну и ту же базу.
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ReceiptDbContext>(o => o.UseInMemoryDatabase(databaseName));
        var cache = new InMemoryCommodityCategoryCache(maxSize);
        services.AddSingleton<ICommodityCategoryCache>(cache);
        services.AddSingleton(Options.Create(new CommodityCategoryCacheOptions { MaxSize = maxSize }));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        seed(db);
        db.SaveChanges();

        return (provider, cache);
    }

    private static CommodityCategoryCacheInitializationHostedService CreateHostedService(
        ServiceProvider provider,
        InMemoryCommodityCategoryCache cache,
        int maxSize)
    {
        return new CommodityCategoryCacheInitializationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            cache,
            Options.Create(new CommodityCategoryCacheOptions { MaxSize = maxSize }),
            NullLogger<CommodityCategoryCacheInitializationHostedService>.Instance);
    }

    private static CommodityEntity Commodity(string name, int? categoryId)
    {
        return new CommodityEntity
        {
            Id = Guid.NewGuid(),
            ReceiptId = Guid.NewGuid(),
            Name = name,
            Quantity = 1,
            UnitPrice = 100,
            Nds = 0,
            NdsSum = 0,
            CategoryId = categoryId
        };
    }
}