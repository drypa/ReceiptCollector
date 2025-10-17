using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

internal sealed class MongoReceiptBatchLoader : IMongoReceiptBatchLoader
{
    private readonly IMongoCollection<MongoReceiptDocumentDto> _collection;
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
        _collection = database.GetCollection<MongoReceiptDocumentDto>(settings.Collection);
        _logger = logger;
    }

    public async Task LoadAllAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        }

        using var cursor = await _collection.FindAsync(FilterDefinition<MongoReceiptDocumentDto>.Empty,
            new FindOptions<MongoReceiptDocumentDto> { BatchSize = batchSize }, cancellationToken).ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                _logger.LogInformation("Loaded receipt document {@ReceiptDocument}", document);
            }
        }
    }
}
