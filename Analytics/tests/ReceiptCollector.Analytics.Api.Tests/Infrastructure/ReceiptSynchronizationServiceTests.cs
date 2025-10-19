using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly Guid _userId = Guid.NewGuid();

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
        await SeedMongoAsync(CreateDocument("doc-1"));
        await SeedMongoAsync(CreateDocument("doc-2"));

        await RunSynchronizationAsync();

        await using (var verificationContext = CreateContext())
        {
            var storedAfterFirstRun = await verificationContext.Receipts
                .IgnoreQueryFilters()
                .Include(r => r.Items)
                .ToListAsync();

            Assert.Equal(2, storedAfterFirstRun.Count);
            Assert.All(storedAfterFirstRun, receipt => Assert.Equal(_userId, receipt.UserId));
        }

        await SeedMongoAsync(CreateDocument("doc-3"));

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
        var loader = CreateLoader();
        var syncOptions = Options.Create(new ReceiptSynchronizationOptions
        {
            BatchSize = 10,
            UserId = _userId
        });

        var service = new ReceiptSynchronizationService(
            loader,
            repository,
            context,
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

    private static MongoReceiptDocumentDto CreateDocument(string externalId)
    {
        return new MongoReceiptDocumentDto
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            ExternalId = externalId,
            TicketId = "ticket-id",
            QueryString = "t=20191005T1548&s=1127.00&fn=9282000100254567&i=11401&fp=371532793&n=1",
            Receipt = new MongoReceiptDocumentDto.ReceiptDto
            {
                Datetime = "2019-10-05T15:48:00",
                TimestampSeconds = 1570280880,
                TotalSum = 112700,
                User = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
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
}
