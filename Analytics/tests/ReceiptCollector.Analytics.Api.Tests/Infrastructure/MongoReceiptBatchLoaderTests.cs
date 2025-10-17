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
            CreateDocument("630f8d8087e76c7685c2113a"),
            CreateDocument("630f8d8087e76c7685c2113b")
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
            Kind = "kkt",
            Operation = new MongoReceiptDocumentDto.OperationDto
            {
                Date = "2022-07-30T16:41",
                Type = 1,
                Sum = 9998
            },
            Qr = "sample-qr",
            Query = new MongoReceiptDocumentDto.QueryDto
            {
                OperationType = 1,
                Sum = 9998,
                DocumentId = 57686,
                FiscMachineId = "9960440301394381",
                FiscalSign = "2408500310",
                Date = "2022-07-30T16:41"
            },
            Seller = new MongoReceiptDocumentDto.SellerDto
            {
                Name = "ООО \"Лента\"",
                Inn = "7814148471"
            },
            Status = 2,
            Ticket = new MongoReceiptDocumentDto.TicketDto
            {
                Document = new MongoReceiptDocumentDto.TicketDocumentDto
                {
                    Receipt = new MongoReceiptDocumentDto.ReceiptDto
                    {
                        TimestampSeconds = 1659188460,
                        CashTotalSum = 0,
                        Code = 3,
                        CreditSum = 0,
                        ECashTotalSum = 9998,
                        FiscalDocumentNumber = 57686,
                        FiscalDriveNumber = "9960440301394381",
                        FiscalSign = 2408500310,
                        FnsUrl = "www.nalog.ru",
                        Items = new List<MongoReceiptDocumentDto.ReceiptItemDto>
                        {
                            new()
                            {
                                Name = "Напиток BORJOMI",
                                Nds = 1,
                                NdsSum = 833,
                                PaymentType = 4,
                                Price = 4999,
                                ProductType = 1,
                                Quantity = 1,
                                Sum = 4999
                            }
                        },
                        KktRegId = "0005998343044853",
                        Nds10 = 454,
                        Nds18 = 0,
                        OperationType = 1,
                        Operator = "Оператор",
                        PrepaidSum = 0,
                        ProvisionSum = 0,
                        RequestNumber = 291,
                        RetailPlace = "ТК ЛЕНТА-1360",
                        RetailPlaceAddress = "Россия, Москва",
                        ShiftNumber = 223,
                        TotalSum = 9998,
                        User = "ООО \"Лента\"",
                        UserInn = "7814148471"
                    }
                }
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
