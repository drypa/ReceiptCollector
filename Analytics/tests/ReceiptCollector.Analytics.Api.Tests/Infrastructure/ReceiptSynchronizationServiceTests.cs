using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class ReceiptSynchronizationServiceTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer;
    private readonly PostgreSqlContainer _postgresContainer;
    private string _mongoConnectionString = string.Empty;
    private string _postgresConnectionString = string.Empty;
    private readonly string _mongoDatabase = "analytics_sync_db";
    private readonly string _mongoCollection = "receipts";

    public ReceiptSynchronizationServiceTests()
    {
        _mongoContainer = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .WithCleanUp(true)
            .Build();

        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task SynchronizeAsync_imports_new_receipts_only_once()
    {
        var owner1 = ObjectId.GenerateNewId().ToString();
        var owner2 = ObjectId.GenerateNewId().ToString();

        await SeedMongoAsync(CreateDocument("doc-1", owner1));
        await SeedMongoAsync(CreateDocument("doc-2", owner2));

        await RunSynchronizationAsync();

        await using (var verificationContext = CreateContext())
        {
            var users = await verificationContext.Users.ToListAsync();
            Assert.Equal(2, users.Count);
            foreach (var user in users)
            {
                Assert.Equal("<Unknown user>", user.Name);
            }

            var storedAfterFirstRun = await verificationContext.Receipts
                .IgnoreQueryFilters()
                .Include(r => r.Items)
                .ToListAsync();

            Assert.Equal(2, storedAfterFirstRun.Count);
            Assert.All(storedAfterFirstRun, receipt =>
            {
                var matchingUser = users.Single(u => u.Id == receipt.UserId);
                Assert.Contains(matchingUser.ExternalId, new[] { owner1, owner2 });
            });
        }

        await SeedMongoAsync(CreateDocument("doc-3", owner1));

        await RunSynchronizationAsync();

        await using (var verificationContext = CreateContext())
        {
            var storedAfterSecondRun = await verificationContext.Receipts
                .IgnoreQueryFilters()
                .ToListAsync();

            Assert.Equal(3, storedAfterSecondRun.Count);
        }
    }

    public async Task InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        await _postgresContainer.StartAsync();

        _mongoConnectionString = _mongoContainer.GetConnectionString();
        _postgresConnectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _mongoContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    private async Task RunSynchronizationAsync()
    {
        await using var context = CreateContext();
        var repository = new ReceiptRepository(context);
        var merchantRepository = new MerchantRepository(context);
        var userRepository = new UserRepository(context);
        var loader = CreateLoader();
        var syncOptions = Options.Create(new ReceiptSynchronizationOptions
        {
            BatchSize = 10
        });

        var service = new ReceiptSynchronizationService(
            loader,
            repository,
            merchantRepository,
            userRepository,
            syncOptions,
            NullLogger<ReceiptSynchronizationService>.Instance);

        await service.SynchronizeAsync(CancellationToken.None);
    }

    private MongoReceiptBatchLoader CreateLoader()
    {
        var options = Options.Create(new MongoReceiptSourceOptions
        {
            ConnectionString = _mongoConnectionString,
            Database = _mongoDatabase,
            Collection = _mongoCollection
        });

        return new MongoReceiptBatchLoader(options, NullLogger<MongoReceiptBatchLoader>.Instance);
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_postgresConnectionString);

        return new ReceiptDbContext(builder.Options);
    }

    private async Task SeedMongoAsync(MongoReceiptDocumentDto document)
    {
        var client = new MongoClient(_mongoConnectionString);
        var database = client.GetDatabase(_mongoDatabase);
        var collection = database.GetCollection<MongoReceiptDocumentDto>(_mongoCollection);
        await collection.InsertOneAsync(document);
    }

    private static MongoReceiptDocumentDto CreateDocument(string externalId, string owner)
    {
        return new MongoReceiptDocumentDto
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            ExternalId = externalId,
            TicketId = "ticket-id",
            QueryString = "t=20191005T1548&s=1127.00&fn=9282000100254567&i=11401&fp=371532793&n=1",
            Owner = owner,
            Seller = new MongoReceiptDocumentDto.SellerDto
            {
                Inn = GenerateInn(externalId),
                Name = $"ООО \"СДЕЛАЙ СВОИМИ РУКАМИ {externalId}\""
            },
            Receipt = new MongoReceiptDocumentDto.ReceiptDto
            {
                Datetime = "2019-10-05T15:48:00",
                TimestampSeconds = 1570280880,
                TotalSum = 112700,
                User = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
                RetailPlaceAddress = "117556 г. Москва, Варшавское шоссе, 97",
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
                }
            }
        };
    }

    private static string GenerateInn(string seed)
    {
        var digits = seed.Where(char.IsDigit).ToArray();
        if (digits.Length >= 10)
        {
            return new string(digits.Take(10).ToArray());
        }

        var inn = new string(digits);
        if (inn.Length < 10)
        {
            inn = inn.PadRight(10, '0');
        }

        return inn;
    }
}
