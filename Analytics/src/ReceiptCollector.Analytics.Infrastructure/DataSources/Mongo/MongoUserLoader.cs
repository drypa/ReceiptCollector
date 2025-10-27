using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

internal sealed class MongoUserLoader : IMongoUserLoader
{
    private readonly IMongoCollection<MongoUserDocumentDto> _collection;
    private readonly ILogger<MongoUserLoader> _logger;

    public MongoUserLoader(IOptions<MongoReceiptSourceOptions> options, ILogger<MongoUserLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value ?? throw new InvalidOperationException("Mongo receipt options are not configured.");

        if (string.IsNullOrWhiteSpace(settings.ConnectionString) ||
            string.IsNullOrWhiteSpace(settings.Database))
        {
            throw new InvalidOperationException("Mongo user source options are incomplete.");
        }

        var usersCollection = string.IsNullOrWhiteSpace(settings.UsersCollection)
            ? "system_users"
            : settings.UsersCollection;

        var client = new MongoClient(settings.ConnectionString);
        var database = client.GetDatabase(settings.Database);
        _collection = database.GetCollection<MongoUserDocumentDto>(usersCollection);
        _logger = logger;
    }

    public async Task<IReadOnlyList<MongoUserDocumentDto>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var users = await _collection
            .Find(FilterDefinition<MongoUserDocumentDto>.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var user in users)
        {
            _logger.LogDebug("Loaded user document {@UserDocument}", user);
        }

        return users;
    }
}
