namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

public sealed record CommodityItemDto(
    Guid Id,
    string MerchantName,
    Guid ReceiptId,
    DateTime PurchasedAt,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    int? CategoryId,
    string? CategoryName);
