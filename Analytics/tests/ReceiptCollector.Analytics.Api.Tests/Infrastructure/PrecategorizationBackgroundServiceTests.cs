using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты фонового сервиса предкатегоризации (шаг 5 плана, ADR-027 п.6, D4–D7, D12, D14, FR-2/FR-4/FR-8).
/// Время управляется <see cref="FakeTimeProvider"/>, логика — на фейках репозитория/кэша/ИИ.
/// </summary>
public sealed class PrecategorizationBackgroundServiceTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Does_not_start_session_before_idle_timeout()
    {
        await using var harness = new Harness();
        harness.Monitor.Idle = false;

        await harness.Service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.Equal(0, harness.Repository.CandidatesCalls);

        harness.Monitor.Idle = true;
        harness.Time.Advance(CheckInterval);
        await WaitUntilAsync(() => harness.Repository.CandidatesCalls == 1);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Processes_only_batch_limit_candidates_per_session()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 2,
            IntervalBetweenCalls = TimeSpan.Zero
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(5);
        harness.Ai.Behavior = _ => new AiSuggestionResult(CommodityCategory.Dairy);

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 2);

        Assert.Equal(2, harness.Repository.Added.Count);
        Assert.Equal(1, harness.Repository.CandidatesCalls);
        Assert.Equal(2, harness.Ai.Calls.Count);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stops_after_current_position_when_activity_appears()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.Zero
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(5);
        harness.Ai.Behavior = _ =>
        {
            // FR-2.5: запрос пришёл во время сессии — после текущей позиции новых не берём.
            harness.Monitor.Idle = false;
            return new AiSuggestionResult(CommodityCategory.Dairy);
        };

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 1);
        await Task.Delay(50);

        Assert.Single(harness.Repository.Added);
        Assert.Equal(1, harness.Repository.CandidatesCalls);

        // Следующий цикл проверки простоя не запускает новую сессию.
        harness.Time.Advance(CheckInterval);
        await Task.Delay(50);
        Assert.Equal(1, harness.Repository.CandidatesCalls);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Interval_between_calls_is_applied_only_after_ai_call()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.FromSeconds(10)
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(2);
        harness.Ai.Behavior = _ => new AiSuggestionResult(CommodityCategory.Dairy);

        await harness.Service.StartAsync(CancellationToken.None);

        // FR-2.5: результат вызова ИИ сохраняется сразу, не дожидаясь паузы.
        await WaitUntilAsync(() => harness.Ai.Calls.Count == 1);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 1);

        // D4/FR-2.4: пока IntervalBetweenCalls не выдержана, второго вызова ИИ нет.
        await Task.Delay(50);
        Assert.Single(harness.Ai.Calls);

        await AdvanceUntilAsync(harness.Time, () => harness.Ai.Calls.Count == 2, TimeSpan.FromSeconds(1));

        var timestamps = harness.Ai.CallTimestamps;
        Assert.Equal(2, timestamps.Count);
        Assert.True(
            timestamps[1] - timestamps[0] >= TimeSpan.FromSeconds(10),
            $"Между вызовами ИИ ожидалось не менее 10 с, получено {timestamps[1] - timestamps[0]}.");

        await WaitUntilAsync(() => harness.Repository.Added.Count == 2);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Existing_and_cache_hits_create_proposals_without_ai_and_without_delay()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.FromSeconds(10)
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(2);
        // D1: existing-шаг приоритетнее кэша; для cache-hit ИИ тоже не вызывается.
        harness.Repository.KnownCategories["Товар 1"] = CommodityCategory.Food;
        harness.Cache.Add("товар 2", CommodityCategory.Bakery);

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 2);

        Assert.Empty(harness.Ai.Calls);
        Assert.Equal(CommodityCategory.Food, harness.Repository.Added[0].Category);
        Assert.Equal(CommodityCategory.Bakery, harness.Repository.Added[1].Category);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Undefined_and_ai_errors_do_not_create_rows_and_move_cursor()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.Zero
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(3);
        harness.Ai.Behavior = name => name switch
        {
            "Товар 1" => new AiSuggestionResult(CommodityCategory.Undefined),
            "Товар 2" => throw new AiClientException("boom"),
            _ => new AiSuggestionResult(CommodityCategory.Dairy)
        };

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Ai.Calls.Count == 3 && harness.Repository.Added.Count == 1);

        var added = Assert.Single(harness.Repository.Added);
        Assert.Equal("Товар 3", added.Candidate.Name);
        Assert.Equal(CommodityCategory.Dairy, added.Category);

        // D14: курсор сдвинулся на последнего обработанного (включая Undefined/ошибку).
        harness.Time.Advance(CheckInterval);
        await WaitUntilAsync(() => harness.Repository.ReceivedCursors.Count == 2);
        Assert.Equal(new CandidateCursor("Товар 3", harness.Repository.Candidates.Last().MerchantId),
            harness.Repository.ReceivedCursors[1]);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_name_is_skipped_with_warning_but_moves_cursor()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.Zero
        });
        harness.Monitor.Idle = true;
        var merchantId = Guid.NewGuid();
        harness.Repository.Candidates = new[]
        {
            new CandidateCursor(new string('x', 257), merchantId),
            new CandidateCursor("Товар 2", merchantId)
        };
        harness.Ai.Behavior = _ => new AiSuggestionResult(CommodityCategory.Dairy);

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 1);

        Assert.Equal("Товар 2", harness.Repository.Added[0].Candidate.Name);
        Assert.True(harness.Logger.WarningCount >= 1);

        harness.Time.Advance(CheckInterval);
        await WaitUntilAsync(() => harness.Repository.ReceivedCursors.Count == 2);
        Assert.Equal(new CandidateCursor("Товар 2", merchantId), harness.Repository.ReceivedCursors[1]);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Candidate_selection_error_does_not_crash_service_and_next_cycle_retries()
    {
        await using var harness = new Harness();
        harness.Monitor.Idle = true;
        harness.Repository.CandidatesException = new InvalidOperationException("db is down");

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.CandidatesCalls == 1 && harness.Logger.ErrorCount >= 1);
        Assert.True(harness.Logger.ErrorCount >= 1);

        harness.Repository.CandidatesException = null;
        harness.Repository.Candidates = Candidates(1);
        harness.Ai.Behavior = _ => new AiSuggestionResult(CommodityCategory.Dairy);

        harness.Time.Advance(CheckInterval);
        await WaitUntilAsync(() => harness.Repository.CandidatesCalls == 2);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Disabled_service_does_not_run_sessions()
    {
        await using var harness = new Harness(new PrecategorizationOptions { Enabled = false });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(1);

        await harness.Service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        harness.Time.Advance(CheckInterval);
        await Task.Delay(50);

        Assert.Equal(0, harness.Repository.CandidatesCalls);
        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Empty_ai_base_url_creates_existing_and_cache_rows_with_single_warning()
    {
        await using var harness = new Harness(
            new PrecategorizationOptions
            {
                IdleTimeout = IdleTimeout,
                IdleCheckInterval = CheckInterval,
                BatchLimit = 10,
                IntervalBetweenCalls = TimeSpan.Zero
            },
            new AiOptions { BaseUrl = string.Empty });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(3);
        harness.Repository.KnownCategories["Товар 1"] = CommodityCategory.Food;
        harness.Cache.Add("товар 2", CommodityCategory.Bakery);

        await harness.Service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => harness.Repository.Added.Count == 2 && harness.Logger.WarningCount == 1);

        Assert.Empty(harness.Ai.Calls);
        Assert.Equal(1, harness.Logger.WarningCount); // D5: предупреждение один раз на сессию
        Assert.Equal(2, harness.Repository.Added.Count);

        await harness.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_stops_session_gracefully_and_keeps_written_rows()
    {
        await using var harness = new Harness(new PrecategorizationOptions
        {
            IdleTimeout = IdleTimeout,
            IdleCheckInterval = CheckInterval,
            BatchLimit = 10,
            IntervalBetweenCalls = TimeSpan.FromSeconds(10)
        });
        harness.Monitor.Idle = true;
        harness.Repository.Candidates = Candidates(2);
        harness.Ai.Behavior = _ => new AiSuggestionResult(CommodityCategory.Dairy);

        await harness.Service.StartAsync(CancellationToken.None);

        // Первая позиция обработана и сохранена, идёт пауза перед следующим вызовом ИИ.
        await WaitUntilAsync(() => harness.Ai.Calls.Count == 1 && harness.Repository.Added.Count == 1);

        await harness.Service.StopAsync(CancellationToken.None);

        // FR-2.8: остановка без исключений, записанная строка сохранена, второго вызова ИИ не было.
        Assert.Single(harness.Repository.Added);
        Assert.Equal("Товар 1", harness.Repository.Added[0].Candidate.Name);
        Assert.Single(harness.Ai.Calls);
    }

    private static IReadOnlyCollection<CandidateCursor> Candidates(int count)
    {
        var merchantId = Guid.NewGuid();
        return Enumerable.Range(1, count)
            .Select(i => new CandidateCursor($"Товар {i}", merchantId))
            .ToList();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for the expected background service state.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Продвигает виртуальное время шагами, пока условие не выполнится. Нужно потому, что таймер
    /// паузы создаётся рабочим потоке сервиса: однократный <c>Advance</c> может случиться раньше.
    /// </summary>
    private static async Task AdvanceUntilAsync(
        FakeTimeProvider time,
        Func<bool> condition,
        TimeSpan virtualStep,
        int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for the expected background service state.");
            }

            time.Advance(virtualStep);
            await Task.Delay(10);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness(PrecategorizationOptions? options = null, AiOptions? aiOptions = null)
        {
            Ai.Time = Time;

            var services = new ServiceCollection();
            services.AddSingleton<IPrecategorizedCommodityRepository>(Repository);
            services.AddSingleton<ICommodityCategoryCache>(Cache);
            services.AddSingleton<IAiClient>(Ai);
            _provider = services.BuildServiceProvider();

            Service = new PrecategorizationBackgroundService(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(options ?? new PrecategorizationOptions
                {
                    IdleTimeout = IdleTimeout,
                    IdleCheckInterval = CheckInterval,
                    BatchLimit = 50,
                    IntervalBetweenCalls = TimeSpan.Zero
                }),
                Options.Create(aiOptions ?? new AiOptions { BaseUrl = "http://ai" }),
                Monitor,
                Time,
                Logger);
        }

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

        public FakeActivityMonitor Monitor { get; } = new();

        public FakeRepository Repository { get; } = new();

        public FakeCache Cache { get; } = new();

        public FakeAiClient Ai { get; } = new();

        public RecordingLogger<PrecategorizationBackgroundService> Logger { get; } = new();

        public PrecategorizationBackgroundService Service { get; }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            await _provider.DisposeAsync();
        }
    }

    private sealed class FakeActivityMonitor : IApiActivityMonitor
    {
        private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;

        public bool Idle { get; set; }

        public DateTimeOffset LastActivityUtc => _lastActivity;

        public void Touch()
        {
            Idle = false;
            _lastActivity = DateTimeOffset.UtcNow;
        }

        public bool IsIdle(TimeSpan threshold) => Idle;
    }

    private sealed class FakeRepository : IPrecategorizedCommodityRepository
    {
        public IReadOnlyCollection<CandidateCursor> Candidates { get; set; } = Array.Empty<CandidateCursor>();

        public Exception? CandidatesException { get; set; }

        public int CandidatesCalls { get; private set; }

        public List<CandidateCursor?> ReceivedCursors { get; } = new();

        public Dictionary<string, CommodityCategory> KnownCategories { get; } = new(StringComparer.Ordinal);

        public List<(CandidateCursor Candidate, CommodityCategory Category)> Added { get; } = new();

        public Task<IReadOnlyCollection<CandidateCursor>> GetCandidatesAsync(
            CandidateCursor? after,
            int limit,
            CancellationToken cancellationToken = default)
        {
            CandidatesCalls++;
            ReceivedCursors.Add(after);

            if (CandidatesException is not null)
            {
                throw CandidatesException;
            }

            return Task.FromResult(Candidates);
        }

        public Task<CommodityCategory?> GetKnownCategoryByNameAsync(
            string exactName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(KnownCategories.TryGetValue(exactName, out var category)
                ? (CommodityCategory?)category
                : null);

        public Task<bool> TryAddProposalAsync(
            CandidateCursor candidate,
            CommodityCategory category,
            CancellationToken cancellationToken = default)
        {
            Added.Add((candidate, category));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyCollection<PrecategorizedCommodity>> GetPageAsync(
            int limit, int offset, string? search, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetPageCountAsync(string? search, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PrecategorizedCommodity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PrecategorizedOperationOutcome> UpdateProposedCategoryAsync(
            Guid id, CommodityCategory category, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ConfirmPrecategorizedResult> ConfirmAsync(
            Guid id, CommodityCategory category, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PrecategorizedOperationOutcome> DeleteProposedAsync(
            Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, CommodityCategory>> GetCategoriesByExactNamesAsync(
            IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeCache : ICommodityCategoryCache
    {
        private readonly Dictionary<string, CommodityCategory> _items = new(StringComparer.Ordinal);

        public int Count => _items.Count;

        public void Add(string normalizedName, CommodityCategory category) => _items[normalizedName] = category;

        public bool TryGet(string normalizedName, out CommodityCategory category)
            => _items.TryGetValue(normalizedName, out category);

        public bool TryAdd(string normalizedName, CommodityCategory category)
        {
            if (_items.ContainsKey(normalizedName))
            {
                return false;
            }

            _items[normalizedName] = category;
            return true;
        }
    }

    private sealed class FakeAiClient : IAiClient
    {
        public Func<string, AiSuggestionResult> Behavior { get; set; }
            = _ => new AiSuggestionResult(CommodityCategory.Other);

        public List<string> Calls { get; } = new();

        /// <summary>Виртуальное время на момент каждого вызова (для проверки паузы D4/FR-2.4).</summary>
        public List<DateTimeOffset> CallTimestamps { get; } = new();

        public TimeProvider? Time { get; set; }

        public Task<AiSuggestionResult> SuggestCategoryAsync(
            string productName,
            IReadOnlyCollection<string> categoryNames,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(productName);
            CallTimestamps.Add(Time?.GetUtcNow() ?? DateTimeOffset.UtcNow);
            return Task.FromResult(Behavior(productName));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }

        public int ErrorCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            switch (logLevel)
            {
                case LogLevel.Warning:
                    WarningCount++;
                    break;
                case LogLevel.Error:
                    ErrorCount++;
                    break;
            }
        }
    }
}
