using MongoDB.Bson;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Юнит-тесты ReceiptOwnerResolver (D3) без реальной MongoDB:
/// резолвинг owner по ticketId, кэширование результата и негативное
/// кэширование null, пропуск loader при отсутствии внешнего id.
/// </summary>
public sealed class ReceiptOwnerResolverTests
{
    private const string OwnerHex = "507f1f77bcf86cd799439011";

    [Fact]
    public async Task ResolveAsync_returns_owner_hex_by_ticket_id()
    {
        var loader = new FakeReceiptRequestLoader(OwnerHex);
        var resolver = new ReceiptOwnerResolver(loader);

        var owner = await resolver.ResolveAsync(CreateDocument("ticket-1"), CancellationToken.None);

        Assert.Equal(OwnerHex, owner);
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_caches_result_second_call_does_not_hit_loader()
    {
        var loader = new FakeReceiptRequestLoader(OwnerHex);
        var resolver = new ReceiptOwnerResolver(loader);
        var document = CreateDocument("ticket-1");

        var first = await resolver.ResolveAsync(document, CancellationToken.None);
        var second = await resolver.ResolveAsync(document, CancellationToken.None);

        Assert.Equal(OwnerHex, first);
        Assert.Equal(first, second);
        // D3: кэш ConcurrentDictionary — второй ResolveAsync не обращается к loader.
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_negative_caching_caches_null()
    {
        var loader = new FakeReceiptRequestLoader(ownerHex: null);
        var resolver = new ReceiptOwnerResolver(loader);
        var document = CreateDocument("ticket-without-owner");

        var first = await resolver.ResolveAsync(document, CancellationToken.None);
        var second = await resolver.ResolveAsync(document, CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        // Негативное кэширование: null кэшируется, loader вызывается один раз.
        Assert.Equal(1, loader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_null_external_id_without_loader_call()
    {
        var loader = new FakeReceiptRequestLoader(OwnerHex);
        var resolver = new ReceiptOwnerResolver(loader);

        // Документ без ключа id (ExternalId == null) — владелец не определяется,
        // обращение к receipt_requests не требуется.
        var owner = await resolver.ResolveAsync(CreateDocument(ticketId: null), CancellationToken.None);

        Assert.Null(owner);
        Assert.Equal(0, loader.CallCount);
    }

    private static RawTicketDocument CreateDocument(string? ticketId)
    {
        var bson = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId()
        };

        if (ticketId is not null)
        {
            bson["id"] = ticketId;
        }

        return RawTicketDocument.FromBsonDocument(bson);
    }

    private sealed class FakeReceiptRequestLoader : IMongoReceiptRequestLoader
    {
        private readonly string? _ownerHex;

        public FakeReceiptRequestLoader(string? ownerHex)
        {
            _ownerHex = ownerHex;
        }

        public int CallCount { get; private set; }

        public Task<string?> ResolveOwnerHexAsync(string? ticketId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_ownerHex);
        }
    }
}