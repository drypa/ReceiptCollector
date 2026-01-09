namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record ReceiptDetailsDto(
    Guid Id,
    MerchantDto Merchant,
    decimal TotalAmount,
    DateTime PurchasedAt,
    IReadOnlyCollection<ReceiptItemDto> Items);

public sealed record ReceiptItemDto(string Name, decimal Quantity, decimal UnitPrice, decimal TotalPrice, Guid? CategoryId);
