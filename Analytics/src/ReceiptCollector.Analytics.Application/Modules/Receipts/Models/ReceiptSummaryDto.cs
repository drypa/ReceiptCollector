namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record ReceiptSummaryDto(Guid Id, string Merchant, decimal TotalAmount, DateTime PurchasedAt);
