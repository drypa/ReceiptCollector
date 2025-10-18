namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class ReceiptEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime PurchasedAt { get; set; }
    public List<CommodityEntity> Items { get; set; } = new();
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
}
