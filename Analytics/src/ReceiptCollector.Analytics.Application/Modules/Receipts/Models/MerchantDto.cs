namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

public sealed record MerchantDto(Guid Id, string Name, int Category, string? Address, string? Inn);