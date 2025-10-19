using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationService
{
    private readonly IMongoReceiptBatchLoader _batchLoader;
    private readonly IReceiptRepository _receiptRepository;
    private readonly IUserRepository _userRepository;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;
    private readonly ILogger<ReceiptSynchronizationService> _logger;

    public ReceiptSynchronizationService(
        IMongoReceiptBatchLoader batchLoader,
        IReceiptRepository receiptRepository,
        IUserRepository userRepository,
        IOptions<ReceiptSynchronizationOptions> options,
        ILogger<ReceiptSynchronizationService> logger)
    {
        _batchLoader = batchLoader ?? throw new ArgumentNullException(nameof(batchLoader));
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var settings = _options.Value ?? throw new InvalidOperationException("Receipt synchronization options are not configured.");

        if (settings.BatchSize <= 0)
        {
            throw new InvalidOperationException("Receipt synchronization batch size must be positive.");
        }

        var batchSize = settings.BatchSize;
        var skip = 0;
        var imported = 0;

        _logger.LogInformation("Starting receipt synchronization.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var documents = await _batchLoader.LoadBatchAsync(skip, batchSize, cancellationToken).ConfigureAwait(false);

            if (documents.Count == 0)
            {
                break;
            }

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var user = await ResolveUserAsync(document, cancellationToken).ConfigureAwait(false);
                    var receipt = MongoReceiptMapper.Map(document, user.Id);
                    await _receiptRepository.AddAsync(receipt, cancellationToken).ConfigureAwait(false);
                    imported++;
                }
                catch (ReceiptAlreadyExistsException)
                {
                    _logger.LogDebug("Receipt {ReceiptExternalId} already exists, skipping.", document.ExternalId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import receipt {ReceiptExternalId}.", document.ExternalId);
                }
            }

            skip += documents.Count;
        }

        _logger.LogInformation("Receipt synchronization completed. Imported {ImportedCount} receipts.", imported);
    }

    private async Task<User> ResolveUserAsync(MongoReceiptDocumentDto document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.Owner))
        {
            throw new InvalidOperationException("Receipt document does not contain owner identifier.");
        }

        return await _userRepository.GetOrCreateAsync(document.Owner, cancellationToken).ConfigureAwait(false);
    }
}
