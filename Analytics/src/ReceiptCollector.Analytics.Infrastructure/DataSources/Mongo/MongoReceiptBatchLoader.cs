using System.Collections.Generic;
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
        var processed = 0;
        while (true)
        {
            var batch = await LoadBatchAsync(processed, batchSize, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var document in batch)
            {
                _logger.LogInformation("Loaded receipt document {@ReceiptDocument}", document);
            }

            processed += batch.Count;
        }
    }

    public async Task<IReadOnlyList<MongoReceiptDocumentDto>> LoadBatchAsync(int skip, int batchSize, CancellationToken cancellationToken)
    {
        if (skip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be non-negative.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
        }

        var documents = await _collection
            .Find(FilterDefinition<MongoReceiptDocumentDto>.Empty)
            .Skip(skip)
            .Limit(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return documents;
    }
}
