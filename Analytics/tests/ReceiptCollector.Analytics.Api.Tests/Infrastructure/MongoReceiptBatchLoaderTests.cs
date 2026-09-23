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
    private readonly string _collectionName = "raw_tickets";

    public MongoReceiptBatchLoaderTests()
    {
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task LoadPageAsync_walks_all_pages_with_keyset_pagination()
    {
        var ids = Enumerable.Range(1, 5).Select(_ => ObjectId.GenerateNewId()).ToArray();
        await SeedAsync(ids.Select(id => CreateRawTicket(id, $"doc-{id}")));

        var loader = CreateLoader();
        var allExternalIds = new List<string>();

        var afterId = ObjectId.Empty;
        while (true)
        {
            var page = await loader.LoadPageAsync(afterId, 2, CancellationToken.None);
            if (page.Count == 0)
            {
                break;
            }

            allExternalIds.AddRange(page.Select(d => d.ExternalId!));
            afterId = page[^1].MongoId;
        }

        Assert.Equal(ids.Length, allExternalIds.Count);
        // Детерминированный порядок: документы приходят по возрастанию _id.
        Assert.Equal(ids.Select(id => $"doc-{id}"), allExternalIds);
    }

    [Fact]
    public async Task LoadPageAsync_respects_batch_size_and_after_id()
    {
        var ids = Enumerable.Range(1, 4).Select(_ => ObjectId.GenerateNewId()).ToArray();
        await SeedAsync(ids.Select(id => CreateRawTicket(id, $"doc-{id}")));

        var loader = CreateLoader();

        var firstPage = await loader.LoadPageAsync(ObjectId.Empty, 2, CancellationToken.None);
        Assert.Equal(2, firstPage.Count);

        var secondPage = await loader.LoadPageAsync(firstPage[^1].MongoId, 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Count);
        Assert.NotEqual(firstPage[0].MongoId, secondPage[0].MongoId);

        var emptyPage = await loader.LoadPageAsync(secondPage[^1].MongoId, 2, CancellationToken.None);
        Assert.Empty(emptyPage);
    }

    [Fact]
    public async Task LoadPageAsync_excludes_documents_with_explicit_null_ticket()
    {
        var fulfilledId = ObjectId.GenerateNewId();
        var notFulfilledId = ObjectId.GenerateNewId();

        await SeedAsync(
        [
            CreateRawTicket(fulfilledId, "fulfilled"),
            CreateRawTicket(notFulfilledId, "not-fulfilled", withPayload: false)
        ]);

        var loader = CreateLoader();
        var page = await loader.LoadPageAsync(ObjectId.Empty, 10, CancellationToken.None);

        var document = Assert.Single(page);
        Assert.Equal(fulfilledId, document.MongoId);
        Assert.Equal("fulfilled", document.ExternalId);
    }

    [Fact]
    public async Task LoadPageAsync_excludes_documents_without_ticket_key()
    {
        // Требование 1 (D7): MongoDB-семантика $ne: null — отсутствующий ключ трактуется
        // как null, поэтому документы БЕЗ ключа ticket (legacy-формат 2020) не возвращаются.
        // Проверяем это поведение явно.
        var legacyDocument = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["id"] = "legacy-2020-id",
            ["receipt"] = new BsonDocument
            {
                ["datetime"] = "2019-10-05T15:48:00",
                ["totalsum"] = BsonInt64.Create(112700),
                ["userinn"] = "5003042456"
            }
        };

        await SeedAsync([legacyDocument]);

        var loader = CreateLoader();
        var page = await loader.LoadPageAsync(ObjectId.Empty, 10, CancellationToken.None);

        Assert.Empty(page);
    }

    [Fact]
    public async Task LoadPageAsync_normalizes_camelCase_keys()
    {
        var id = ObjectId.GenerateNewId();
        // Верхнеуровневые ключи — в нижнем регистре (как пишет backend-драйвер 1.17.9),
        // вложенные camelCase (дань legacy-миграциям при наполнении коллекции).
        var camelCaseDocument = new BsonDocument
        {
            ["_id"] = id,
            ["id"] = "uuid-ticket-id",
            ["qr"] = "t=20191005T1548&s=1127.00&fn=9282000100254567&i=11401&fp=371532793&n=1",
            ["ticket"] = new BsonDocument
            {
                ["Document"] = new BsonDocument
                {
                    ["Receipt"] = new BsonDocument
                    {
                        ["DateTime"] = BsonInt64.Create(1570280880),
                        ["TotalSum"] = BsonInt64.Create(112700),
                        ["UserInn"] = "5003042456"
                    }
                }
            }
        };

        await SeedAsync([camelCaseDocument]);

        var loader = CreateLoader();
        var page = await loader.LoadPageAsync(ObjectId.Empty, 10, CancellationToken.None);

        var document = Assert.Single(page);
        Assert.Equal("uuid-ticket-id", document.ExternalId);
        Assert.Equal(id, document.MongoId);
        Assert.Equal(112700, document.GetPayload()!.TotalSumMinor);
        Assert.NotNull(document.GetPurchasedAt());
    }

    [Fact]
    public async Task LoadPageAsync_applies_nd18_alias_to_nds18()
    {
        var id = ObjectId.GenerateNewId();
        await SeedAsync(
        [
            new BsonDocument
            {
                ["_id"] = id,
                ["id"] = "doc-with-nd18",
                ["ticket"] = new BsonDocument
                {
                    ["document"] = new BsonDocument
                    {
                        ["receipt"] = new BsonDocument
                        {
                            ["totalsum"] = BsonInt64.Create(112700),
                            ["nd18"] = BsonInt64.Create(18783)
                        }
                    }
                }
            }
        ]);

        var loader = CreateLoader();
        var page = await loader.LoadPageAsync(ObjectId.Empty, 10, CancellationToken.None);

        var document = Assert.Single(page);
        Assert.Equal(18783, document.GetPayload()!.Nds18Minor);
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

    private MongoReceiptBatchLoader CreateLoader()
    {
        var options = Options.Create(new MongoReceiptSourceOptions
        {
            ConnectionString = _connectionString,
            Database = _databaseName,
            Collection = _collectionName
        });

        return new MongoReceiptBatchLoader(options, new TestLogger<MongoReceiptBatchLoader>());
    }

    private async Task SeedAsync(IEnumerable<BsonDocument> documents)
    {
        var client = new MongoClient(_connectionString);
        var database = client.GetDatabase(_databaseName);
        var collection = database.GetCollection<BsonDocument>(_collectionName);
        await collection.InsertManyAsync(documents);
    }

    private static BsonDocument CreateRawTicket(ObjectId id, string externalId, bool withPayload = true)
    {
        var document = new BsonDocument
        {
            ["_id"] = id,
            ["status"] = withPayload ? 2 : 1,
            ["id"] = externalId
        };

        if (withPayload)
        {
            document["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["datetime"] = BsonInt64.Create(1570280880),
                        ["totalsum"] = BsonInt64.Create(112700),
                        ["userinn"] = "5003042456",
                        ["user"] = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
                        ["operator"] = "18 ИВАНОВА",
                        ["retailplaceaddress"] = "117556 г. Москва, Варшавское шоссе, 97",
                        ["items"] = new BsonArray(new[]
                        {
                            new BsonDocument
                            {
                                ["name"] = "ПАНЕЛЬ  250Х3000",
                                ["quantity"] = 6.0,
                                ["price"] = BsonInt64.Create(14800),
                                ["sum"] = BsonInt64.Create(88800),
                                ["nds"] = 20,
                                ["ndssum"] = BsonInt64.Create(14800)
                            }
                        })
                    }
                }
            };
        }
        else
        {
            // «Не fulfilled»: backend пишет ticket: null явно.
            document["ticket"] = BsonNull.Value;
        }

        return document;
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}