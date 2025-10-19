namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public sealed class Commodity
{
    public Guid Id { get; }

    public Guid ReceiptId { get; }

    public string Name { get; }

    public decimal Quantity { get; }

    public decimal UnitPrice { get; }

    public int Nds { get; }

    public decimal NdsSum { get; }

    public decimal TotalPrice => Quantity * UnitPrice;

    public Category? Category { get; private set; }

    public Commodity(
        Guid id,
        Guid receiptId,
        string name,
        decimal quantity,
        decimal unitPrice,
        int nds,
        decimal ndsSum,
        Category? category = null)
    {
        Id = id;
        ReceiptId = receiptId;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Nds = nds;
        NdsSum = ndsSum;
        Category = category;
    }

    public void AssignCategory(Category category)
    {
        Category = category;
    }
}
