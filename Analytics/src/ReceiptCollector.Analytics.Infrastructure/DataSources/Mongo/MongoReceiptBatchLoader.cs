using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

internal sealed class MongoReceiptBatchLoader : IMongoReceiptBatchLoader
{
    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly ILogger<MongoReceiptBatchLoader> _logger;

    public MongoReceiptBatchLoader(IOptions<MongoReceiptSourceOptions> options, ILogger<MongoReceiptBatchLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value ?? throw new InvalidOperationException("Mongo receipt options are not configured.");

        if (string.IsNullOrWhiteSpace(settings.ConnectionString) ||
            string.IsNullOrWhiteSpace(settings.Database) ||
            string.IsNullOrWhiteSpace(settings.Collection))
        {
            throw new InvalidOperationException("Mongo receipt source options are incomplete.");
        }

        var client = new MongoClient(settings.ConnectionString);
        var database = client.GetDatabase(settings.Database);
        // D2: читаем сырые BsonDocument (формат raw_tickets не гарантирован по регистру ключей),
        // нормализация выполняется при обёртке в RawTicketDocument.
        _collection = database.GetCollection<BsonDocument>(settings.Collection);
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawTicketDocument>> LoadPageAsync(
        ObjectId afterId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        }

        // D7: keyset-пагинация по _id — детерминированный обход без skip, страницы не «плывут»
        // при параллельной вставке новых тикетов воркером backend.
        // $ne: null отбрасывает документы с явным ticket: null («не fulfilled», backend пишет
        // именно null; см. GetRawReceiptWithoutTicket). Документы без ключа ticket
        // (legacy-формат с верхнеуровневым receipt) попадают в выборку — их обрабатывает
        // RawTicketDocument.GetPayload().
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Gt("_id", afterId),
            Builders<BsonDocument>.Filter.Ne("ticket", BsonNull.Value));

        var documents = await _collection
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .Limit(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return documents.Select(RawTicketDocument.FromBsonDocument).ToList();
    }
}