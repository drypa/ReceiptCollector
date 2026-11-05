using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
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
    private readonly string _mongoCollection = "raw_tickets";

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

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto
            {
                Id = ObjectId.Parse(owner1),
                Name = "First Owner",
                TelegramId = 101
            },
            new MongoUserDocumentDto
            {
                Id = ObjectId.Parse(owner2),
                Name = "Second Owner",
                TelegramId = 202
            }
        });

        await SeedRawTicketAsync(ticketId: "doc-1", ownerHex: owner1);
        await SeedRawTicketAsync(ticketId: "doc-2", ownerHex: owner2);

        await using (var firstContext = CreateContext())
        {
            var service = CreateService(firstContext, userLoader);
            await service.SynchronizeAsync(CancellationToken.None);

            var storedAfterFirstRun = await firstContext.Receipts
                .IgnoreQueryFilters()
                .Include(r => r.Items)
                .ToListAsync();

            Assert.Equal(2, storedAfterFirstRun.Count);
        }

        // Новый чек для того же владельца, но другая дата покупки (иначе natural key
        // совпадёт с doc-1 и дедупликация корректно пропустит его — см. отдельный тест).
        await SeedRawTicketAsync(ticketId: "doc-3", ownerHex: owner1, qrTime: "t=20260923T1100");

        await using (var secondContext = CreateContext())
        {
            var service = CreateService(secondContext, userLoader);
            await service.SynchronizeAsync(CancellationToken.None);

            var storedAfterSecondRun = await secondContext.Receipts
                .IgnoreQueryFilters()
                .ToListAsync();

            // doc-1 и doc-2 повторно не импортируются (external_id), doc-3 — новый.
            Assert.Equal(3, storedAfterSecondRun.Count);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_imports_nested_payload_with_correct_fields()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        await SeedRawTicketAsync(ticketId: "new-format-uuid", ownerHex: owner, qrTime: "t=20260922T0841");

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        var stored = await context.Receipts
            .IgnoreQueryFilters()
            .Include(r => r.Items)
            .Include(r => r.Merchant)
            .ToListAsync();

        var receipt = Assert.Single(stored);
        Assert.Equal("new-format-uuid", receipt.ExternalId);
        Assert.Equal(1127.00m, receipt.TotalAmount);
        Assert.Equal(new DateTime(2026, 9, 22, 8, 41, 0, DateTimeKind.Utc), receipt.PurchasedAt);
        Assert.Equal("ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"", receipt.Merchant.Name);
        Assert.Equal("117556 г. Москва, Варшавское шоссе, 97", receipt.Merchant.Address);
        var item = Assert.Single(receipt.Items);
        Assert.Equal("ПАНЕЛЬ  250Х3000", item.Name);
        Assert.Equal(6, item.Quantity);
    }

    [Fact]
    public async Task SynchronizeAsync_skips_documents_without_payload()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Документ «не fulfilled» (ticket: null) — исключается фильтром лоадера.
        await SeedRawTicketAsync(ticketId: "doc-1", ownerHex: owner, withPayload: false);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        var stored = await context.Receipts.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(stored);
    }

    [Fact]
    public async Task SynchronizeAsync_skips_when_owner_not_resolved()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // В receipt_requests нет записи по ticket_id "doc-without-owner" — владелец не резолвится.
        await SeedRawTicketAsync(ticketId: "doc-without-owner", ownerHex: null);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        var stored = await context.Receipts.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(stored);
    }

    [Fact]
    public async Task SynchronizeAsync_skips_electronic_receipts_with_nil_owner()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Электронный чек: owner = NilObjectID (известное ограничение backend, TODO electronic.go).
        await SeedRawTicketAsync(ticketId: "electronic-doc", ownerHex: null);
        await SeedReceiptRequestAsync("electronic-doc", MongoReceiptRequestLoader.NilObjectId);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        var stored = await context.Receipts.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(stored);
    }

    [Fact]
    public async Task SynchronizeAsync_excludes_legacy_documents_without_ticket_key()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Legacy-документ 2020: верхнеуровневый receipt, ключа ticket нет вовсе.
        // Требование 1: MongoDB-семантика $ne: null исключает такие документы из выборки —
        // они не попадают ни в лоадер, ни в синхронизацию (натуральный ключ здесь совпал
        // бы с новым форматом, но это не имеет значения — документ не читается).
        var legacyDocument = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["id"] = "legacy-2020-id",
            ["receipt"] = new BsonDocument
            {
                ["datetime"] = "2019-10-05T15:48:00",
                ["totalsum"] = BsonInt64.Create(112700),
                ["userinn"] = "5003042456",
                ["user"] = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\""
            }
        };

        // Новый формат: тот же физический чек (same natural key), external_id = UUID тикета.
        await SeedMongoRawTicketAsync(legacyDocument);
        await SeedReceiptRequestAsync("legacy-2020-id", ObjectId.Parse(owner));
        await SeedRawTicketAsync(ticketId: "new-format-uuid-same-receipt", ownerHex: owner, qrTime: "t=20191005T1548");

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        // Импортирован только новый формат; legacy-документ без ключа ticket пропущен.
        var stored = await context.Receipts.IgnoreQueryFilters().ToListAsync();
        var receipt = Assert.Single(stored);
        Assert.Equal("new-format-uuid-same-receipt", receipt.ExternalId);
    }

    [Fact]
    public async Task SynchronizeAsync_deduplicates_when_same_physical_receipt_added_twice()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Один физический чек добавлен дважды: два raw-документа с разными id,
        // но одинаковым владельцем, датой покупки (QR t) и суммой.
        await SeedRawTicketAsync(ticketId: "dup-uuid-1", ownerHex: owner, qrTime: "t=20191005T1548");
        await SeedRawTicketAsync(ticketId: "dup-uuid-2", ownerHex: owner, qrTime: "t=20191005T1548");

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var service = CreateService(context, userLoader);
        await service.SynchronizeAsync(CancellationToken.None);

        var stored = await context.Receipts.IgnoreQueryFilters().ToListAsync();
        Assert.Single(stored);
    }

    // T-05 (ADR-032, C1/FR-M2): автоприменение дефолтной товарной категории магазина
    // при импорте чека — ровно после успешного AddAsync, сбой не валяет цикл.

    [Fact]
    public async Task SynchronizeAsync_applies_merchant_default_category_to_imported_receipt()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Магазин с INN из сидируемого тикета и настроенным дефолтом (ADR-032, A1).
        await SeedMerchantAsync(defaultCommodityCategoryId: (int)CommodityCategory.ReadyMeals);

        await SeedRawTicketAsync(ticketId: "default-doc", ownerHex: owner);

        var commodityRepository = Substitute.For<ICommodityRepository>();
        commodityRepository
            .ApplyMerchantDefaultForReceiptAsync(
                Arg.Any<Guid>(), Arg.Any<CommodityCategory>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await using (var context = CreateContext())
        {
            var service = CreateService(context, userLoader, commodityRepository);
            await service.SynchronizeAsync(CancellationToken.None);
        }

        await using (var verifyContext = CreateContext())
        {
            var receipt = Assert.Single(await verifyContext.Receipts.IgnoreQueryFilters().ToListAsync());

            // Вызов ровно один и с правильной парой: id импортированного чека + дефолт магазина.
            await commodityRepository.Received(1).ApplyMerchantDefaultForReceiptAsync(
                receipt.Id, CommodityCategory.ReadyMeals, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task SynchronizeAsync_does_not_apply_merchant_default_when_not_configured()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Магазин без дефолта (NULL) — импорт идет прежним потоком без обращений к применению (FR-M8).
        await SeedMerchantAsync(defaultCommodityCategoryId: null);

        await SeedRawTicketAsync(ticketId: "no-default-doc", ownerHex: owner);

        var commodityRepository = Substitute.For<ICommodityRepository>();

        await using (var context = CreateContext())
        {
            var service = CreateService(context, userLoader, commodityRepository);
            await service.SynchronizeAsync(CancellationToken.None);
        }

        await using (var verifyContext = CreateContext())
        {
            // Чек импортирован как раньше…
            Assert.Single(await verifyContext.Receipts.IgnoreQueryFilters().ToListAsync());
        }

        // …но операция применения дефолта не вызывалась ни разу.
        await commodityRepository.DidNotReceiveWithAnyArgs()
            .ApplyMerchantDefaultForReceiptAsync(default!, default, default);
    }

    [Fact]
    public async Task SynchronizeAsync_when_apply_merchant_default_fails_import_continues_and_error_is_logged()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        await SeedMerchantAsync(defaultCommodityCategoryId: (int)CommodityCategory.ReadyMeals);

        // Два разных физических чека одного магазина (разные даты покупки — разные natural key).
        await SeedRawTicketAsync(ticketId: "apply-fail-doc-1", ownerHex: owner, qrTime: "t=20191005T1548");
        await SeedRawTicketAsync(ticketId: "apply-fail-doc-2", ownerHex: owner, qrTime: "t=20260923T1100");

        var commodityRepository = Substitute.For<ICommodityRepository>();
        commodityRepository
            .ApplyMerchantDefaultForReceiptAsync(
                Arg.Any<Guid>(), Arg.Any<CommodityCategory>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(Task.FromException<int>(
                new InvalidOperationException("simulated apply failure")));

        var logger = new RecordingLogger<ReceiptSynchronizationService>();

        await using (var context = CreateContext())
        {
            var service = CreateService(context, userLoader, commodityRepository, logger: logger);
            // Сбой применения не должен валить ни чек, ни цикл синхронизации (ADR-032 п. 3).
            await service.SynchronizeAsync(CancellationToken.None);
        }

        await using (var verifyContext = CreateContext())
        {
            var stored = await verifyContext.Receipts.IgnoreQueryFilters().ToListAsync();

            // Оба чека закоммичены: сбой по чеку не откатил импорт и не остановил обработку следующего.
            Assert.Equal(2, stored.Count);
            Assert.Contains("apply-fail-doc-1", stored.Select(r => r.ExternalId));
            Assert.Contains("apply-fail-doc-2", stored.Select(r => r.ExternalId));
        }

        // Применение attempted для обоих чеков (после сбоя первого импорт продолжился).
        await commodityRepository.Received(2).ApplyMerchantDefaultForReceiptAsync(
            Arg.Any<Guid>(), CommodityCategory.ReadyMeals, Arg.Any<CancellationToken>());

        // Сбой залогирован на уровне Error с исключением — по одному на чек.
        Assert.Equal(
            2,
            logger.Entries.Count(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains("Failed to apply merchant default", StringComparison.Ordinal)
                && entry.Exception is not null));
    }

    [Fact]
    public async Task SynchronizeAsync_when_add_fails_does_not_apply_merchant_default()
    {
        var owner = ObjectId.GenerateNewId().ToString();

        var userLoader = new TestMongoUserLoader(new[]
        {
            new MongoUserDocumentDto { Id = ObjectId.Parse(owner), Name = "Owner", TelegramId = 1 }
        });

        // Магазин с дефолтом — применение возможно по данным, но чек не сохранён:
        // блок ADR-032 стоит строго после успешного AddAsync и вызван быть не может.
        await SeedMerchantAsync(defaultCommodityCategoryId: (int)CommodityCategory.ReadyMeals);
        await SeedRawTicketAsync(ticketId: "add-fail-doc", ownerHex: owner);

        var receiptRepository = Substitute.For<IReceiptRepository>();
        receiptRepository
            .When(x => x.AddAsync(Arg.Any<Receipt>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("simulated save failure"));

        var commodityRepository = Substitute.For<ICommodityRepository>();

        await using (var context = CreateContext())
        {
            var service = CreateService(context, userLoader, commodityRepository, receiptRepository);
            // Existing per-receipt catch: цикл синхронизации переживает сбой сохранения.
            await service.SynchronizeAsync(CancellationToken.None);
        }

        await using (var verifyContext = CreateContext())
        {
            Assert.Empty(await verifyContext.Receipts.IgnoreQueryFilters().ToListAsync());
        }

        await commodityRepository.DidNotReceiveWithAnyArgs()
            .ApplyMerchantDefaultForReceiptAsync(default!, default, default);
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

    private IOptions<MongoReceiptSourceOptions> CreateMongoOptions()
    {
        return Options.Create(new MongoReceiptSourceOptions
        {
            ConnectionString = _mongoConnectionString,
            Database = _mongoDatabase,
            Collection = _mongoCollection
        });
    }

    private MongoReceiptBatchLoader CreateReceiptLoader()
    {
        return new MongoReceiptBatchLoader(CreateMongoOptions(), NullLogger<MongoReceiptBatchLoader>.Instance);
    }

    private IReceiptOwnerResolver CreateOwnerResolver()
    {
        // D3: реальный резолвинг через receipt_requests (Mongo-контейнер поднят).
        var requestLoader = new MongoReceiptRequestLoader(
            CreateMongoOptions(),
            NullLogger<MongoReceiptRequestLoader>.Instance);
        return new ReceiptOwnerResolver(requestLoader);
    }

    private ReceiptSynchronizationService CreateService(
        ReceiptDbContext context,
        IMongoUserLoader userLoader,
        ICommodityRepository? commodityRepository = null,
        IReceiptRepository? receiptRepository = null,
        ILogger<ReceiptSynchronizationService>? logger = null)
    {
        var repository = receiptRepository ?? new ReceiptRepository(context);
        var merchantRepository = new MerchantRepository(context);
        var userRepository = new UserRepository(context);
        var receiptLoader = CreateReceiptLoader();
        var syncOptions = Options.Create(new ReceiptSynchronizationOptions
        {
            BatchSize = 10
        });

        return new ReceiptSynchronizationService(
            receiptLoader,
            repository,
            merchantRepository,
            commodityRepository ?? new CommodityRepository(context),
            userRepository,
            userLoader,
            CreateOwnerResolver(),
            syncOptions,
            logger ?? NullLogger<ReceiptSynchronizationService>.Instance);
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_postgresConnectionString)
            .UseSnakeCaseNamingConvention();

        return new ReceiptDbContext(builder.Options);
    }

    /// <summary>Создаёт и сохраняет документ raw_tickets + связанную запись receipt_requests.</summary>
    private async Task SeedRawTicketAsync(
        string ticketId,
        string? ownerHex,
        bool withPayload = true,
        string? qrTime = "t=20191005T1548")
    {
        await SeedMongoRawTicketAsync(CreateRawTicket(ticketId, withPayload, qrTime));

        if (ownerHex is not null)
        {
            await SeedReceiptRequestAsync(ticketId, ObjectId.Parse(ownerHex));
        }
    }

    private async Task SeedMongoRawTicketAsync(BsonDocument document)
    {
        var client = new MongoClient(_mongoConnectionString);
        var database = client.GetDatabase(_mongoDatabase);
        var collection = database.GetCollection<BsonDocument>(_mongoCollection);
        await collection.InsertOneAsync(document);
    }

    private async Task SeedReceiptRequestAsync(string ticketId, ObjectId owner, bool deleted = false)
    {
        var client = new MongoClient(_mongoConnectionString);
        var database = client.GetDatabase(_mongoDatabase);
        var collection = database.GetCollection<BsonDocument>("receipt_requests");
        await collection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["ticket_id"] = ticketId,
            ["owner"] = owner,
            ["deleted"] = deleted
        });
    }

    /// <summary>
    /// Магазин с INN из <see cref="CreateRawTicket"/> («5003042456») до запуска синхронизации:
    /// <c>ResolveMerchantAsync</c> резолвит его по INN и приносит домен с дефолтом
    /// (безопасное чтение — T-02). null = дефолт не задан.
    /// </summary>
    private async Task SeedMerchantAsync(int? defaultCommodityCategoryId)
    {
        await using var context = CreateContext();
        context.Merchants.Add(new MerchantEntity
        {
            Id = Guid.NewGuid(),
            Name = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
            Category = MerchantCategory.Undefined,
            Address = "117556 г. Москва, Варшавское шоссе, 97",
            Inn = "5003042456",
            DefaultCommodityCategoryId = defaultCommodityCategoryId
        });
        await context.SaveChangesAsync();
    }

    private static BsonDocument CreateRawTicket(string ticketId, bool withPayload = true, string? qrTime = "t=20191005T1548")
    {
        var document = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["status"] = withPayload ? 2 : 1,
            ["id"] = ticketId,
            ["qr"] = qrTime
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
            document["ticket"] = BsonNull.Value;
        }

        return document;
    }

    private sealed class TestMongoUserLoader : IMongoUserLoader
    {
        private readonly IReadOnlyList<MongoUserDocumentDto> _users;

        public TestMongoUserLoader(IEnumerable<MongoUserDocumentDto> users)
        {
            _users = users.Select(Clone).ToArray();
        }

        public Task<IReadOnlyList<MongoUserDocumentDto>> LoadAllAsync(CancellationToken cancellationToken)
        {
            var snapshot = _users.Select(Clone).ToArray();
            return Task.FromResult((IReadOnlyList<MongoUserDocumentDto>)snapshot);
        }

        private static MongoUserDocumentDto Clone(MongoUserDocumentDto source)
        {
            return new MongoUserDocumentDto
            {
                Id = source.Id,
                Name = source.Name,
                TelegramId = source.TelegramId
            };
        }
    }

    /// <summary>
    /// Логгер, сохраняющий записи (стиль <c>TestLogger</c> из
    /// <see cref="MongoReceiptBatchLoaderTests"/>) — для проверки «сбой залогирован».
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
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
