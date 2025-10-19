using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class Receipt
{
    public Guid Id { get; }

    public Guid UserId { get; }

    public string Merchant { get; }
    
    public string ExternalId { get; }

    public decimal TotalAmount { get; }     

    public DateTime PurchasedAt { get; }

    private readonly List<Commodity> _items = new();

    public IReadOnlyCollection<Commodity> Items => _items;

    public Receipt(
        Guid id,
        Guid userId,
        string merchant,
        decimal totalAmount,
        DateTime purchasedAt,
        string externalId,
        IEnumerable<Commodity>? items = null)
    {
        Id = id;
        UserId = userId;
        Merchant = merchant;
        TotalAmount = totalAmount;
        PurchasedAt = purchasedAt;
        ExternalId = externalId;
        if (items is not null)
        {
            _items.AddRange(items);
        }
    }

    public void AddItem(Commodity item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public void AddItems(IEnumerable<Commodity> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var item in items)
        {
            AddItem(item);
        }
    }

}
