using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

/// <summary>
/// Чтение коллекции <c>receipt_requests</c> для разрешения владельца чека (D3):
/// запрос выполняется по <c>ticket_id</c> = внешний id тикета из raw_tickets.
/// </summary>
internal sealed class MongoReceiptRequestLoader : IMongoReceiptRequestLoader
{
    /// <summary>NilObjectID — известное ограничение backend для электронных чеков (TODO backend/workers/electronic.go).</summary>
    internal static readonly ObjectId NilObjectId = ObjectId.Parse("000000000000000000000000");

    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly ILogger<MongoReceiptRequestLoader> _logger;

    public MongoReceiptRequestLoader(
        IOptions<MongoReceiptSourceOptions> options,
        ILogger<MongoReceiptRequestLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value ?? throw new InvalidOperationException("Mongo receipt options are not configured.");

        if (string.IsNullOrWhiteSpace(settings.ConnectionString) ||
            string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new InvalidOperationException("Mongo receipt source options are incomplete.");
        }

        var client = new MongoClient(settings.ConnectionString);
        var database = client.GetDatabase(settings.Database);
        _collection = database.GetCollection<BsonDocument>("receipt_requests");
        _logger = logger;
    }

    /// <summary>
    /// Возвращает hex-строку owner по ticket_id, либо null, если владелец не найден
    /// (удалённые запросы, пустой owner или NilObjectID — электронные чеки).
    /// </summary>
    public async Task<string?> ResolveOwnerHexAsync(string? ticketId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return null;
        }

        // D3: ищем незаделетенные запросы по ticket_id с непустым owner.
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("ticket_id", ticketId),
            Builders<BsonDocument>.Filter.Ne("deleted", true),
            Builders<BsonDocument>.Filter.Ne("owner", BsonNull.Value));

        var documents = await _collection
            .Find(filter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? firstOwner = null;
        string? owner = null;

        foreach (var document in documents)
        {
            if (!document.TryGetValue("owner", out var ownerValue) || !ownerValue.IsObjectId)
            {
                continue;
            }

            var ownerId = ownerValue.AsObjectId;

            // NilObjectID — электронные чеки: владельца нет, пропускаем (известное ограничение backend).
            if (ownerId == NilObjectId)
            {
                continue;
            }

            if (owner is null)
            {
                owner = ownerId.ToString();
                firstOwner = owner;
            }
            else if (!string.Equals(owner, ownerId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // Rare case: один тикет добавлен несколькими владельцами — берём первого (D3).
                _logger.LogWarning(
                    "Receipt request {TicketId} has multiple owners ({FirstOwner}, {AnotherOwner}); using the first one.",
                    ticketId, firstOwner, ownerId);
            }
        }

        return owner;
    }
}

/// <summary>Поиск владельца чека в receipt_requests по id тикета (D3).</summary>
internal interface IMongoReceiptRequestLoader
{
    Task<string?> ResolveOwnerHexAsync(string? ticketId, CancellationToken cancellationToken);
}