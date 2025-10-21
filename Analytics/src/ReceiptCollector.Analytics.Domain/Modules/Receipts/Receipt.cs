using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class Receipt
{
    public Guid Id { get; }

    public Guid UserId { get; }

    public Guid MerchantId { get; }
    
    public string ExternalId { get; }

    public decimal TotalAmount { get; }     

    public DateTime PurchasedAt { get; }

    private readonly List<Commodity> _items = new();

    public IReadOnlyCollection<Commodity> Items => _items;

    public Receipt(
        Guid id,
        Guid userId,
        Guid merchantId,
        decimal totalAmount,
        DateTime purchasedAt,
        string externalId,
        IEnumerable<Commodity>? items = null)
    {
        Id = id;
        UserId = userId;
        MerchantId = merchantId;
        TotalAmount = totalAmount;
        PurchasedAt = purchasedAt;
        ExternalId = externalId;
        if (items is not null)
        {
            _items.AddRange(items);
        }
    }
}
