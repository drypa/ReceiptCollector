using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationService
{
    private readonly IMongoReceiptBatchLoader _batchLoader;
    private readonly IReceiptRepository _receiptRepository;
    private readonly IMerchantRepository _merchantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMongoUserLoader _userLoader;
    private readonly IReceiptOwnerResolver _ownerResolver;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;
    private readonly ILogger<ReceiptSynchronizationService> _logger;

    public ReceiptSynchronizationService(
        IMongoReceiptBatchLoader batchLoader,
        IReceiptRepository receiptRepository,
        IMerchantRepository merchantRepository,
        IUserRepository userRepository,
        IMongoUserLoader userLoader,
        IReceiptOwnerResolver ownerResolver,
        IOptions<ReceiptSynchronizationOptions> options,
        ILogger<ReceiptSynchronizationService> logger)
    {
        _batchLoader = batchLoader ?? throw new ArgumentNullException(nameof(batchLoader));
        _receiptRepository = receiptRepository ?? throw new ArgumentNullException(nameof(receiptRepository));
        _merchantRepository = merchantRepository ?? throw new ArgumentNullException(nameof(merchantRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _userLoader = userLoader ?? throw new ArgumentNullException(nameof(userLoader));
        _ownerResolver = ownerResolver ?? throw new ArgumentNullException(nameof(ownerResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var settings = _options.Value ??
                       throw new InvalidOperationException("Receipt synchronization options are not configured.");

        if (settings.BatchSize <= 0)
        {
            throw new InvalidOperationException("Receipt synchronization batch size must be positive.");
        }

        await SynchronizeUsersAsync(cancellationToken).ConfigureAwait(false);

        var batchSize = settings.BatchSize;
        // D7: keyset-пагинация по _id; первая страница — ObjectId.Empty.
        var afterId = ObjectId.Empty;
        var imported = 0;

        _logger.LogInformation("Starting receipt synchronization.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var documents = await _batchLoader
                .LoadPageAsync(afterId, batchSize, cancellationToken)
                .ConfigureAwait(false);

            if (documents.Count == 0)
            {
                break;
            }

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // D8: «не fulfilled» — ни ticket.document.receipt, ни верхнеуровневого receipt нет.
                if (document.GetPayload() is null)
                {
                    _logger.LogInformation("Receipt {ReceiptMongoId} is not fulfilled, skipping.", document.MongoId);
                    continue;
                }

                // D5: без даты покупки чек импортировать нельзя (edge case задачи) — пропускаем.
                if (document.GetPurchasedAt() is null)
                {
                    _logger.LogWarning("Receipt {ReceiptMongoId} has no purchase timestamp, skipping.", document.MongoId);
                    continue;
                }

                try
                {
                    var ownerHex = await ResolveOwnerHexAsync(document, cancellationToken).ConfigureAwait(false);
                    if (ownerHex is null)
                    {
                        // D3: владелец не найден (в т.ч. NilObjectID электронных чеков) — пропуск без падения.
                        _logger.LogWarning(
                            "Receipt {ReceiptMongoId} ({ReceiptExternalId}): owner not resolved, skip.",
                            document.MongoId, document.ExternalId);
                        continue;
                    }

                    var user = await ResolveUserAsync(ownerHex, cancellationToken).ConfigureAwait(false);
                    var externalId = document.ExternalId ?? document.MongoId.ToString();

                    // D4: идемпотентность повторных циклов по внешнему id тикета.
                    var existReceipt = await _receiptRepository
                        .GetByExternalIdAsync(externalId, user.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (existReceipt is not null)
                    {
                        _logger.LogInformation("Receipt {ReceiptExternalId} already exists, skipping.", externalId);
                        continue;
                    }

                    Merchant merchant = await ResolveMerchantAsync(document, cancellationToken).ConfigureAwait(false);
                    var receipt = MongoReceiptMapper.Map(document, user.Id, merchant.Id);

                    // D4: естественный ключ (user_id, purchased_at, total_amount) — cross-формат 2020
                    // (legacy external_id != id тикета) и сценарий «чек добавлен дважды».
                    var existingByNaturalKey = await _receiptRepository
                        .GetByNaturalKeyAsync(user.Id, receipt.PurchasedAt, receipt.TotalAmount, cancellationToken)
                        .ConfigureAwait(false);
                    if (existingByNaturalKey is not null)
                    {
                        _logger.LogInformation(
                            "Receipt {ReceiptExternalId} already exists by natural key, skipping.",
                            externalId);
                        continue;
                    }

                    await _receiptRepository.AddAsync(receipt, cancellationToken).ConfigureAwait(false);
                    imported++;
                }
                catch (ReceiptAlreadyExistsException)
                {
                    _logger.LogWarning("Failed to save receipt {ReceiptExternalId} already exists, skipping.", document.ExternalId);
                }
                catch (DbUpdateException ex)
                {
                    // D4: страховка от гонки (unique-индекс естественного ключа).
                    _logger.LogError(ex, "Failed to save receipt {ReceiptExternalId} (database update failed, likely a duplicate).", document.ExternalId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import receipt {ReceiptExternalId}.", document.ExternalId);
                }
            }

            afterId = documents[^1].MongoId;
        }

        _logger.LogInformation("Receipt synchronization completed. Imported {ImportedCount} receipts.", imported);
    }

    private async Task SynchronizeUsersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting user synchronization.");

        var userDocuments = await _userLoader.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var document in userDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var externalId = document.Id.ToString();
            var name = string.IsNullOrWhiteSpace(document.Name) ? "<Unknown user>" : document.Name.Trim();
            var telegramId = document.TelegramId.GetValueOrDefault();

            var existing = await _userRepository
                .GetByExternalIdAsync(externalId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                var user = new User(Guid.NewGuid(), name, externalId, telegramId);
                await _userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.Equals(existing.Name, name, StringComparison.Ordinal) || existing.TelegramId != telegramId)
            {
                var updated = new User(existing.Id, name, externalId, telegramId);
                await _userRepository.AddAsync(updated, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("User synchronization completed. Processed {UserCount} users.", userDocuments.Count);
    }

    private async Task<string?> ResolveOwnerHexAsync(RawTicketDocument document, CancellationToken cancellationToken)
    {
        // D3: владелец определяется через receipt_requests по id тикета (в raw_tickets owner отсутствует).
        return await _ownerResolver.ResolveAsync(document, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Merchant> ResolveMerchantAsync(RawTicketDocument document,
        CancellationToken cancellationToken)
    {
        var payload = document.GetPayload();

        if (string.IsNullOrWhiteSpace(payload?.UserInn))
        {
            throw new InvalidOperationException("Receipt seller is not specified.");
        }

        var inn = payload.UserInn.Trim();
        var existing = await _merchantRepository.GetByInnAsync(inn, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var name = MongoReceiptMapper.GetMerchantName(document);
        var address = ExtractAddress(payload);

        var merchant = new Merchant(Guid.NewGuid(), name, MerchantCategory.Undefined, address, inn);
        await _merchantRepository.AddAsync(merchant, cancellationToken).ConfigureAwait(false);
        return merchant;
    }

    private static string? ExtractAddress(ReceiptPayload? payload)
    {
        var address = payload?.RetailPlaceAddress;

        if (!string.IsNullOrWhiteSpace(address))
        {
            return address;
        }

        return null;
    }

    private async Task<User> ResolveUserAsync(string ownerHex,
        CancellationToken cancellationToken)
    {
        var existing = await _userRepository.GetByExternalIdAsync(ownerHex, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // Открытый вопрос 3: поведение авто-создания "<Unknown user>" сохраняется.
        var user = new User(Guid.NewGuid(), "<Unknown user>", ownerHex, 0);
        await _userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
        return user;
    }
}