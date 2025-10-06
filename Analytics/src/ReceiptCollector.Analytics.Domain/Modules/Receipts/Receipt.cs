namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class Receipt
{
    public Guid Id { get; }

    public Guid UserId { get; }

    public string Merchant { get; }

    public decimal TotalAmount { get; }

    public DateTime PurchasedAt { get; }

    private readonly List<ReceiptItem> _items = new();

    public IReadOnlyCollection<ReceiptItem> Items => _items;

    public Receipt(Guid id, Guid userId, string merchant, decimal totalAmount, DateTime purchasedAt)
    {
        Id = id;
        UserId = userId;
        Merchant = merchant;
        TotalAmount = totalAmount;
        PurchasedAt = purchasedAt;
    }

    public void AddItem(ReceiptItem item)
    {
        _items.Add(item);
    }
}
