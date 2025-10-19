using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationService
{
    private readonly IMongoReceiptBatchLoader _batchLoader;
    private readonly IReceiptRepository _receiptRepository;
    private readonly ReceiptDbContext _dbContext;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;
    private readonly ILogger<ReceiptSynchronizationService> _logger;

    public ReceiptSynchronizationService(
        IMongoReceiptBatchLoader batchLoader,
        IReceiptRepository receiptRepository,
        ReceiptDbContext dbContext,
        IOptions<ReceiptSynchronizationOptions> options,
        ILogger<ReceiptSynchronizationService> logger)
    {
        _batchLoader = batchLoader ?? throw new ArgumentNullException(nameof(batchLoader));
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var settings = _options.Value ?? throw new InvalidOperationException("Receipt synchronization options are not configured.");

        if (settings.UserId is null || settings.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Receipt synchronization user id is not configured.");
        }

        if (settings.BatchSize <= 0)
        {
            throw new InvalidOperationException("Receipt synchronization batch size must be positive.");
        }

        var userId = settings.UserId.Value;
        var batchSize = settings.BatchSize;

        var offset = await GetExistingReceiptsCountAsync(userId, cancellationToken).ConfigureAwait(false);
        var skip = offset;
        var imported = 0;

        _logger.LogInformation("Starting receipt synchronization. Existing receipts: {ExistingCount}", offset);

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
                    var receipt = MongoReceiptMapper.Map(document, userId);
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

    private async Task<int> GetExistingReceiptsCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        _dbContext.SetCurrentUser(userId);
        try
        {
            return await _dbContext.Receipts.CountAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbContext.ClearCurrentUser();
        }
    }
}
