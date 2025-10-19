using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class ReceiptEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime PurchasedAt { get; set; }
    public List<CommodityEntity> Items { get; set; } = new();
    
    internal static ReceiptEntity Create(Receipt receipt)
    {
        return new ReceiptEntity
        {
            Id = receipt.Id,
            UserId = receipt.UserId,
            Merchant = receipt.Merchant,
            ExternalId = receipt.ExternalId,
            TotalAmount = receipt.TotalAmount,
            PurchasedAt = receipt.PurchasedAt,
            Items = receipt.Items.Select(CommodityEntity.Create).ToList()
        };
    }
    
    internal Receipt MapToDomain()
    {
        var items = Items.Select(item =>
        {
            Category? category = item.CategoryId.HasValue && item.CategoryName is not null
                ? new Category(item.CategoryId.Value, item.CategoryName)
                : null;

            return new Commodity(
                item.Id,
                Id,
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.Nds,
                item.NdsSum,
                category);
        }).ToList();

        return new Receipt(
            Id,
            UserId,
            Merchant,
            TotalAmount,
            PurchasedAt,
            ExternalId,
            items);
    }
    
}

internal sealed class CommodityEntity
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int Nds { get; set; }
    public decimal NdsSum { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public ReceiptEntity Receipt { get; set; } = null!;
    
    internal static CommodityEntity Create(Commodity commodity)
    {
        return new CommodityEntity
        {
            Id = commodity.Id,
            ReceiptId = commodity.ReceiptId,
            Name = commodity.Name,
            Quantity = commodity.Quantity,
            UnitPrice = commodity.UnitPrice,
            Nds = commodity.Nds,
            NdsSum = commodity.NdsSum,
            CategoryId = commodity.Category?.Id,
            CategoryName = commodity.Category?.Name ?? null
        };
    }
}
