namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record ReceiptSummaryDto(Guid Id, string Merchant, Guid MerchantId, decimal TotalAmount, DateTime PurchasedAt);
