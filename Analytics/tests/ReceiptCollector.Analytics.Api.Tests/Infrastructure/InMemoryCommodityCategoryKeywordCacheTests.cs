using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты singleton-кэша снимка слов (T-03, ADR-035 решение E1, п. 6) на фейковом репозитории:
/// горячий путь без БД, атомарная подмена снимка, деградация при сбое перечитывания
/// («грязный» флаг + ленивое перечитывание), прогрев hosted-сервисом.
/// </summary>
public sealed class InMemoryCommodityCategoryKeywordCacheTests
{
    [Fact]
    public async Task GetSnapshot_first_call_loads_lazily_then_never_touches_repository()
    {
        var repository = new FakeKeywordRepository();
        repository.Returns(Word("изюм"));
        var (cache, provider) = CreateCache(repository);
        await using var _ = provider;

        // До прогрева кэш «грязный» — первое чтение перечитывает само (ленивость).
        var first = cache.GetSnapshot();
        Assert.Equal("изюм", Assert.Single(first.Items).Word);
        Assert.Equal(1, repository.GetAllCalls);

        await cache.RefreshAsync();
        Assert.Equal(2, repository.GetAllCalls);

        // Горячий путь: чтения после загрузки репозиторий не касаются (NFR-1).
        var second = cache.GetSnapshot();
        var third = cache.GetSnapshot();
        Assert.Same(second, third);
        Assert.Equal(2, repository.GetAllCalls);
    }

    [Fact]
    public async Task RefreshAsync_swaps_snapshot_and_previous_reference_stays_valid()
    {
        var repository = new FakeKeywordRepository();
        repository.Returns(Word("изюм"));
        var (cache, provider) = CreateCache(repository);
        await using var _ = provider;

        await cache.RefreshAsync();
        var before = cache.GetSnapshot();
        Assert.Equal(1, repository.GetAllCalls); // чтение после прогрева — без БД

        repository.Returns(Word("курага"), Word("чернослив"));
        await cache.RefreshAsync();
        var after = cache.GetSnapshot();

        // Ссылка, взятая до refresh, остаётся прежней и валидной: снимок неизменяем,
        // подменяется только ссылка (решение E1).
        Assert.NotSame(before, after);
        Assert.Equal("изюм", Assert.Single(before.Items).Word);
        Assert.Equal(2, after.Items.Count);
        Assert.Equal(2, repository.GetAllCalls); // ровно по одному SELECT на явный refresh
    }

    [Fact]
    public async Task Failed_refresh_keeps_old_snapshot_and_next_read_retries()
    {
        var repository = new FakeKeywordRepository();
        repository.Returns(Word("изюм"));
        var (cache, provider) = CreateCache(repository);
        await using var _ = provider;

        await cache.RefreshAsync();
        var before = cache.GetSnapshot();

        // Мутация уже зафиксирована, но перечитывание упало (БД недоступна): refresh не бросает,
        // старый снимок остаётся валидным, флаг «грязный» (ADR-035 п. 6).
        repository.Throws(new InvalidOperationException("database unavailable"));
        await cache.RefreshAsync();
        Assert.Same(before, cache.GetSnapshot()); // чтение ретраит перечитывание и снова деградирует

        // БД вернулась — следующее чтение перечитывается («упал один раз, потом успех», NFR-4).
        repository.Returns(Word("курага"));
        var after = cache.GetSnapshot();
        Assert.NotSame(before, after);
        Assert.Equal("курага", Assert.Single(after.Items).Word);

        // refresh(1) + refresh-сбой(2) + чтение-сбой(3) + чтение-успех(4).
        Assert.Equal(4, repository.GetAllCalls);
    }

    [Fact]
    public async Task Concurrent_reads_during_refresh_never_observe_half_snapshot()
    {
        var oldWords = new[] { Word("изюм"), Word("курага"), Word("чернослив") };
        var newWords = oldWords.Concat(new[] { Word("финик"), Word("банан") }).ToArray();

        var repository = new FakeKeywordRepository();
        repository.Returns(oldWords);
        var (cache, provider) = CreateCache(repository);
        await using var _ = provider;

        await cache.RefreshAsync();
        var before = cache.GetSnapshot();

        // Медленное перечитывание: конкурентные чтения не падают и не блокируются (dirty не выставлен
        // до сбоя), каждый снимок — либо старый целиком, либо новый целиком.
        repository.ReturnsAfterDelay(TimeSpan.FromMilliseconds(50), newWords);
        var refresh = cache.RefreshAsync();

        var seen = new ConcurrentBag<CommodityCategoryKeywordSnapshot>();
        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                seen.Add(cache.GetSnapshot());
            }
        })).ToArray();

        await refresh;
        await Task.WhenAll(readers);
        var after = cache.GetSnapshot();

        Assert.NotEmpty(seen);
        Assert.All(seen, snapshot => Assert.True(
            ReferenceEquals(snapshot, before) || ReferenceEquals(snapshot, after),
            "Чтение увидело снимок, который не является ни прежним, ни новым целиком."));
        Assert.All(seen, snapshot => Assert.InRange(snapshot.Items.Count, oldWords.Length, newWords.Length));
    }

    [Fact]
    public async Task HostedService_with_unavailable_repository_does_not_throw_and_leaves_cache_dirty()
    {
        var repository = new FakeKeywordRepository();
        repository.Throws(new InvalidOperationException("database unavailable"));
        var (cache, provider) = CreateCache(repository);
        await using var _ = provider;

        var hostedService = new CommodityCategoryKeywordCacheInitializationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CommodityCategoryKeywordCacheInitializationHostedService>.Instance);

        // Ошибка прогрева не валит процесс (ADR-035 п. 6).
        await hostedService.StartAsync(CancellationToken.None);
        Assert.Equal(1, repository.GetAllCalls);

        // Кэш остался «грязным»: чтение ретраит перечитывание и при повторном сбое отдаёт
        // пустой снимок без исключения.
        var empty = cache.GetSnapshot();
        Assert.Empty(empty.Items);
        Assert.Equal(2, repository.GetAllCalls);

        // Как только БД доступна — ленивое перечитывание наполняет снимок без рестарта.
        repository.Returns(Word("изюм"));
        var warmed = cache.GetSnapshot();
        Assert.Equal("изюм", Assert.Single(warmed.Items).Word);
    }

    private static (ICommodityCategoryKeywordCache Cache, ServiceProvider Provider) CreateCache(
        FakeKeywordRepository repository)
    {
        // Репозиторий — scoped (как в production DI), кэш — singleton поверх IServiceScopeFactory.
        var services = new ServiceCollection();
        services.AddScoped<ICommodityCategoryKeywordRepository>(_ => repository);
        services.AddSingleton<ICommodityCategoryKeywordCache>(sp => new InMemoryCommodityCategoryKeywordCache(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InMemoryCommodityCategoryKeywordCache>.Instance));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ICommodityCategoryKeywordCache>(), provider);
    }

    private static CommodityCategoryKeyword Word(string word)
        => new(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, word, word);

    /// <summary>
    /// Фейк только под чтение снимка (<see cref="ICommodityCategoryKeywordRepository.GetAllAsync"/>):
    /// счётчик вызовов и управляемое поведение (значение / задержка / сбой). Мутационные методы —
    /// <see cref="NotSupportedException"/>: кэш их не вызывает по контракту.
    /// </summary>
    private sealed class FakeKeywordRepository : ICommodityCategoryKeywordRepository
    {
        private Func<Task<IReadOnlyCollection<CommodityCategoryKeyword>>> _getAllAsync =
            () => Task.FromResult<IReadOnlyCollection<CommodityCategoryKeyword>>(
                Array.Empty<CommodityCategoryKeyword>());

        public int GetAllCalls { get; private set; }

        public void Returns(params CommodityCategoryKeyword[] items)
            => _getAllAsync = () => Task.FromResult<IReadOnlyCollection<CommodityCategoryKeyword>>(items);

        public void ReturnsAfterDelay(TimeSpan delay, params CommodityCategoryKeyword[] items)
            => _getAllAsync = async () =>
            {
                await Task.Delay(delay);
                return (IReadOnlyCollection<CommodityCategoryKeyword>)items;
            };

        public void Throws(Exception exception)
            => _getAllAsync = () =>
                Task.FromException<IReadOnlyCollection<CommodityCategoryKeyword>>(exception);

        public Task<IReadOnlyCollection<CommodityCategoryKeyword>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            GetAllCalls++;
            return _getAllAsync();
        }

        public Task<bool> ExistsAsync(
            CommodityCategory category, KeywordListType listType, string normalizedWord,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The cache reads the snapshot only.");

        public Task<int> CountAsync(
            CommodityCategory category, KeywordListType listType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The cache reads the snapshot only.");

        public Task<bool> AddAsync(
            CommodityCategoryKeyword keyword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The cache does not mutate storage.");

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The cache does not mutate storage.");
    }
}
