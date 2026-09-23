using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal static class MongoReceiptMapper
{
    private const decimal MinorUnitsFactor = 100m;

    /// <summary>
    /// D8: источник — RawTicketDocument/ReceiptPayload; ExternalId = id тикета
    /// (fallback — _id.ToString()), purchasedAt — по D5 (QR t → datetime), totalAmount = totalsum/100
    /// (одинаково для legacy и нового формата — требование консистентности natural key, D4).
    /// </summary>
    public static Receipt Map(RawTicketDocument document, Guid userId, Guid merchantId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var payload = document.GetPayload()
                      ?? throw new InvalidOperationException("Mongo receipt document is missing receipt payload.");

        var externalId = document.ExternalId ?? document.MongoId.ToString();
        var purchasedAt = document.GetPurchasedAt()
                          ?? throw new InvalidOperationException("Receipt payload does not contain purchase timestamp.");
        var totalAmount = ConvertMinorUnits(payload.TotalSumMinor ?? 0);

        var receiptId = CreateDeterministicGuid(externalId);

        var items = payload.Items.Select(item => MapItem(item, receiptId)).ToList();

        return new Receipt(receiptId, userId, merchantId, totalAmount, purchasedAt, externalId, items);
    }

    private static Commodity MapItem(ReceiptItemPayload item, Guid receiptId)
    {
        var itemName = string.IsNullOrWhiteSpace(item.Name) ? "<Unknown item>" : item.Name;

        return new Commodity(
            Guid.NewGuid(),
            receiptId,
            itemName,
            item.QuantityRaw is null ? 0m : Convert.ToDecimal(item.QuantityRaw.Value),
            ConvertMinorUnits(item.PriceMinor ?? 0),
            item.Nds ?? 0,
            ConvertMinorUnits(item.NdsSumMinor ?? 0),
            null);
    }

    private static decimal ConvertMinorUnits(long value) => decimal.Divide(value, MinorUnitsFactor);

    internal static string GetMerchantName(RawTicketDocument document)
    {
        var payload = document.GetPayload();

        if (!string.IsNullOrWhiteSpace(payload?.User))
        {
            return payload.User;
        }

        if (!string.IsNullOrWhiteSpace(document.SellerName))
        {
            return document.SellerName;
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