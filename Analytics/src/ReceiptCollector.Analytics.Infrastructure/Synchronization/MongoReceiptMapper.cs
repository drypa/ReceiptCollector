using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal static class MongoReceiptMapper
{
    private const decimal MinorUnitsFactor = 100m;

    public static Receipt Map(MongoReceiptDocumentDto document, Guid userId, Guid merchantId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var receiptDto = document.Receipt ?? document.Ticket?.Document?.Receipt
                         ?? throw new InvalidOperationException("Mongo receipt document is missing receipt payload.");

        var externalId = string.IsNullOrWhiteSpace(document.ExternalId)
            ? document.Id.ToString()
            : document.ExternalId;

        var purchasedAt = GetPurchasedAt(receiptDto);
        var totalAmount = ConvertMinorUnits(receiptDto.TotalSum);

        var receiptId = string.IsNullOrWhiteSpace(document.ExternalId)
            ? Guid.NewGuid()
            : CreateDeterministicGuid(document.ExternalId);

        var items = receiptDto.Items?.Select(item => MapItem(item, receiptId)).ToList();

        return new Receipt(receiptId, userId, merchantId, totalAmount, purchasedAt, externalId, items);
    }

    private static Commodity MapItem(MongoReceiptDocumentDto.ReceiptItemDto item, Guid receiptId)
    {
        var itemName = string.IsNullOrWhiteSpace(item.Name) ? "<Unknown item>" : item.Name;

        return new Commodity(
            Guid.NewGuid(),
            receiptId,
            itemName,
            Convert.ToDecimal(item.Quantity),
            ConvertMinorUnits(item.Price),
            item.Nds,
            ConvertMinorUnits(item.NdsSum),
            null);
    }

    private static decimal ConvertMinorUnits(long value) => decimal.Divide(value, MinorUnitsFactor);

    private static DateTime GetPurchasedAt(MongoReceiptDocumentDto.ReceiptDto receiptDto)
    {
        if (receiptDto.TimestampSeconds > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(receiptDto.TimestampSeconds).UtcDateTime;
        }

        if (!string.IsNullOrWhiteSpace(receiptDto.Datetime) &&
            DateTime.TryParse(receiptDto.Datetime, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Receipt payload does not contain purchase timestamp.");
    }

    internal static string GetMerchantName(MongoReceiptDocumentDto document)
    {
        var receiptDto = document.Receipt ?? document.Ticket?.Document?.Receipt;

        if (!string.IsNullOrWhiteSpace(receiptDto?.User))
        {
            return receiptDto.User;
        }

        if (!string.IsNullOrWhiteSpace(document.Seller?.Name))
        {
            return document.Seller.Name;
        }

        return "<Unknown merchant>";
    }

    internal static Guid CreateDeterministicGuid(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return new Guid(hash);
    }
}
