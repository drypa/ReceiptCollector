namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record ReceiptDetailsDto(
    Guid Id,
    MerchantDto Merchant,
    decimal TotalAmount,
    DateTime PurchasedAt,
    IReadOnlyCollection<ReceiptItemDto> Items);

/// <summary>Товар в детализации чека. Id — идентификатор товара (commodities.id), нужен для сохранения категорий.</summary>
public sealed record ReceiptItemDto(Guid Id, string Name, decimal Quantity, decimal UnitPrice, decimal TotalPrice, int? CategoryId);
