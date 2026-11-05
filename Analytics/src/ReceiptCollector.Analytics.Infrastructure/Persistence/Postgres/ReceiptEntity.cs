using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class ReceiptEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MerchantId { get; set; }
    public MerchantEntity Merchant { get; set; } = null!;
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
            MerchantId = receipt.MerchantId,
            ExternalId = receipt.ExternalId,
            TotalAmount = receipt.TotalAmount,
            PurchasedAt = NormalizeUtc(receipt.PurchasedAt),
            // CommodityEntity.Create намеренно не заполняет CategorySource: источник есть только
            // у операций применения дефолта (ADR-032, B1); новая позиция — NULL (manual/legacy).
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

            // CategorySource в домен не проектируется: у доменной категории источника нет,
            // маркировка — persistence-деталь (ADR-032, B1; п. 2 «Commodity без изменений»).
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
            MerchantId,
            TotalAmount,
            DateTime.SpecifyKind(PurchasedAt, DateTimeKind.Utc),
            ExternalId,
            items);
    }
    
    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
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

    /// <summary>
    /// Persisted-источник категории (ADR-032, B1): <c>'merchant-default'</c> — проставлено
    /// дефолтом магазина (автоимпорт/дожим, операции T-04); <c>NULL</c> = manual/legacy/неизвестно.
    /// На доменный <see cref="Commodity"/> не маппится (п. 2 ADR-032); существующие пути записи
    /// категории колонку не пишут.
    /// </summary>
    public string? CategorySource { get; set; }

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
            // CategorySource не заполняется: домен источника не знает, новая строка — NULL (ADR-032, B1).
        };
    }
}
