using System.Collections.Concurrent;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

/// <summary>
/// Разрешение владельца чека из raw_tickets через связку
/// raw_tickets.id → receipt_requests.ticket_id → owner (D3).
/// </summary>
internal interface IReceiptOwnerResolver
{
    /// <summary>
    /// Возвращает hex-строку ObjectId владельца (сохраняется как external_id PG-пользователя)
    /// либо null, если владелец не определяется (документ пропускается сервисом с warn-логом).
    /// </summary>
    Task<string?> ResolveAsync(RawTicketDocument document, CancellationToken cancellationToken);
}

internal sealed class ReceiptOwnerResolver : IReceiptOwnerResolver
{
    /// <summary>
    /// Кэш ticketId → ownerHex (null — негативное кэширование допустимо: масштаб тысячи,
    /// кэш переживает циклы синхронизации). Размер ограничен числом чеков.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _cache = new();
    private readonly IMongoReceiptRequestLoader _receiptRequestLoader;

    public ReceiptOwnerResolver(IMongoReceiptRequestLoader receiptRequestLoader)
    {
        _receiptRequestLoader = receiptRequestLoader ?? throw new ArgumentNullException(nameof(receiptRequestLoader));
    }

    public async Task<string?> ResolveAsync(RawTicketDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var ticketId = document.ExternalId;
        if (ticketId is null)
        {
            return null;
        }

        if (_cache.TryGetValue(ticketId, out var cached))
        {
            return cached;
        }

        var ownerHex = await _receiptRequestLoader
            .ResolveOwnerHexAsync(ticketId, cancellationToken)
            .ConfigureAwait(false);
        _cache[ticketId] = ownerHex;
        return ownerHex;
    }
}