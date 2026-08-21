using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using Testcontainers.MongoDb;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class MongoReceiptBatchLoaderTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer;
    private string _connectionString = string.Empty;
    private readonly string _databaseName = "analytics_test_db";
    private readonly string _collectionName = "receipts";

    public MongoReceiptBatchLoaderTests()
    {
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task LoadAllAsync_logs_each_document()
    {
        var client = new MongoClient(_connectionString);
        var database = client.GetDatabase(_databaseName);
        var collection = database.GetCollection<MongoReceiptDocumentDto>(_collectionName);

        await collection.InsertManyAsync(new[]
        {
            CreateDocument("doc-1"),
            CreateDocument("doc-2"),
        });

        var options = Options.Create(new MongoReceiptSourceOptions
        {
            ConnectionString = _connectionString,
            Database = _databaseName,
            Collection = _collectionName
        });

        var logger = new TestLogger<MongoReceiptBatchLoader>();
        var loader = new MongoReceiptBatchLoader(options, logger);

        await loader.LoadAllAsync(batchSize: 1, CancellationToken.None);

        var informationLogs = logger.Entries.Where(entry => entry.Level == LogLevel.Information).ToList();
        Assert.Equal(2, informationLogs.Count);
    }

    [Fact]
    public async Task LoadBatchAsync_respects_skip_and_batch_size()
    {
        var client = new MongoClient(_connectionString);
        var database = client.GetDatabase(_databaseName);
        var collection = database.GetCollection<MongoReceiptDocumentDto>(_collectionName);

        await collection.InsertManyAsync(new[]
        {
            CreateDocument("doc-1"),
            CreateDocument("doc-2"),
            CreateDocument("doc-3"),
        });

        var options = Options.Create(new MongoReceiptSourceOptions
        {
            ConnectionString = _connectionString,
            Database = _databaseName,
            Collection = _collectionName
        });

        var loader = new MongoReceiptBatchLoader(options, new TestLogger<MongoReceiptBatchLoader>());

        var batch = await loader.LoadBatchAsync(1, 2, CancellationToken.None);

        Assert.Equal(2, batch.Count);
        Assert.Equal("doc-2", batch[0].ExternalId);
        Assert.Equal("doc-3", batch[1].ExternalId);
    }

    public async Task InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        _connectionString = _mongoContainer.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _mongoContainer.DisposeAsync();
    }

    private static MongoReceiptDocumentDto CreateDocument(string externalId)
    {
        return new MongoReceiptDocumentDto
        {
            Id = ObjectId.GenerateNewId(),
            ExternalId = externalId,
            TicketId = "ticket-id",
            QueryString = "t=20191005T1548&s=1127.00&fn=9282000100254567&i=11401&fp=371532793&n=1",
            Owner = ObjectId.GenerateNewId().ToString(),
            Receipt = new MongoReceiptDocumentDto.ReceiptDto
            {
                Datetime = "2019-10-05T15:48:00",
                TimestampSeconds = 1570280880,
                CashTotalSum = 0,
                ECashTotalSum = 112700,
                FiscalDocumentNumber = 11401,
                FiscalDriveNumber = "9282000100254567",
                FiscalSign = 371532793,
                Items = new List<MongoReceiptDocumentDto.ReceiptItemDto>
                {
                    new()
                    {
                        Name = "ПАНЕЛЬ  250Х3000",
                        Quantity = 6,
                        Price = 14800,
                        Sum = 88800,
                        Nds = 0,
                        NdsSum = 0
                    },
                    new()
                    {
                        Name = "МОМЕНТ МОНТАЖ 400 ГР",
                        Quantity = 1,
                        Price = 23900,
                        Sum = 23900,
                        Nds = 0,
                        NdsSum = 0
                    }
                },
                RetailPlaceAddress = "117556 г. Москва, Варшавское шоссе, 97",
                User = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
                TotalSum = 112700,
                UserInn = "5003042456"
            }
        };
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
