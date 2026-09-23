using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;
using NSubstitute;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты ReceiptSynchronizationHostedService (D6): Skip-флаг выходит без синхронизации,
/// недоступная БД/сеть не валит процесс (ошибка логируется, цикл продолжается),
/// graceful shutdown по CancellationToken (StopAsync).
/// </summary>
public sealed class ReceiptSynchronizationHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_skips_synchronization_when_skip_flag_is_set()
    {
        var scopeFactory = new TrackingScopeFactory();
        var hosted = new ReceiptSynchronizationHostedService(
            scopeFactory,
            NullLogger<ReceiptSynchronizationHostedService>.Instance,
            Options.Create(new ReceiptSynchronizationOptions { Skip = true }));

        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);

        // D6: при Skip=true цикл не запускается — scope для синхронизации не создаётся.
        Assert.Equal(0, scopeFactory.CreateScopeCount);
    }

    [Fact]
    public async Task ExecuteAsync_survives_unavailable_database_and_keeps_retrying()
    {
        // Postgres недоступен (порт 1 на localhost закрыт, Timeout=1 — быстрый отказ).
        // Критерий приёмки: «сервис не должен падать целиком», стартовая проверка PG — в теле цикла.
        var services = new ServiceCollection();
        services.AddDbContext<ReceiptDbContext>(o => o
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=receipts;Username=u;Password=p;Timeout=1")
            .UseSnakeCaseNamingConvention());

        var provider = services.BuildServiceProvider();
        var logger = new CollectingLogger<ReceiptSynchronizationHostedService>();
        var hosted = new ReceiptSynchronizationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            Options.Create(new ReceiptSynchronizationOptions { Skip = false, IntervalSeconds = 1 }));

        await hosted.StartAsync(CancellationToken.None);

        // Ждём несколько циклов: каждый логирует warn «Unable to connect...» и продолжает работу.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (logger.WarningCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        // Graceful shutdown (D6): StopAsync отменяет токен, цикл завершается без исключений.
        await hosted.StopAsync(CancellationToken.None);

        Assert.True(logger.WarningCount >= 2,
            $"Expected at least 2 retry warnings when PG is unavailable, got {logger.WarningCount}.");
        Assert.Equal(0, logger.ErrorCount);
    }

    [Fact]
    public async Task ExecuteAsync_logs_error_and_keeps_running_when_synchronization_throws()
    {
        // Mongo недоступен (эмуляция: лоадер бросает TimeoutException из LoadPageAsync).
        // Исключение ловится в цикле, логируется, процесс жив, следующий цикл продолжается.
        var services = new ServiceCollection();
        services.AddDbContext<ReceiptDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<IMongoReceiptBatchLoader>(new ThrowingReceiptBatchLoader());
        services.AddSingleton<IMongoUserLoader>(new EmptyUserLoader());
        services.AddSingleton<IReceiptOwnerResolver>(new NullOwnerResolver());
        services.AddSingleton<IReceiptRepository>(Substitute.For<IReceiptRepository>());
        services.AddSingleton<IMerchantRepository>(Substitute.For<IMerchantRepository>());
        services.AddSingleton<IUserRepository>(Substitute.For<IUserRepository>());
        services.AddSingleton(Options.Create(new ReceiptSynchronizationOptions { BatchSize = 10 }));
        services.AddScoped<ReceiptSynchronizationService>();

        var provider = services.BuildServiceProvider();
        var logger = new CollectingLogger<ReceiptSynchronizationHostedService>();
        var hosted = new ReceiptSynchronizationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            Options.Create(new ReceiptSynchronizationOptions { Skip = false, IntervalSeconds = 1 }));

        await hosted.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (logger.ErrorCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        await hosted.StopAsync(CancellationToken.None);

        Assert.True(logger.ErrorCount >= 2,
            $"Expected at least 2 logged sync errors when Mongo is unavailable, got {logger.ErrorCount}.");
    }

    private sealed class TrackingScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            // Skip=true: синхронизация не должна создавать scope вообще.
            CreateScopeCount++;
            throw new InvalidOperationException("A scope must not be created when Skip is set.");
        }
    }

    /// <summary>Эмуляция недоступного Mongo-источника на уровне синхронизации.</summary>
    private sealed class ThrowingReceiptBatchLoader : IMongoReceiptBatchLoader
    {
        public Task<IReadOnlyList<RawTicketDocument>> LoadPageAsync(
            ObjectId afterId,
            int batchSize,
            CancellationToken cancellationToken)
        {
            throw new TimeoutException("Simulated Mongo timeout: source database is unavailable.");
        }
    }

    private sealed class EmptyUserLoader : IMongoUserLoader
    {
        public Task<IReadOnlyList<MongoUserDocumentDto>> LoadAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<MongoUserDocumentDto>>(Array.Empty<MongoUserDocumentDto>());
        }
    }

    private sealed class NullOwnerResolver : IReceiptOwnerResolver
    {
        public Task<string?> ResolveAsync(RawTicketDocument document, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>Считает Warnings/Errors — по ним проверяем ретраи цикла синхронизации (D6).</summary>
    private sealed class CollectingLogger<T> : ILogger<T>
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