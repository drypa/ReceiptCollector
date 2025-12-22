namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record ReceiptSummaryDto(Guid Id, MerchantDto Merchant, decimal TotalAmount, DateTime PurchasedAt);
