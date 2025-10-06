namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class ReceiptItem
{
    public Guid Id { get; }

    public Guid ReceiptId { get; }

    public string Name { get; }

    public decimal Quantity { get; }

    public decimal UnitPrice { get; }

    public decimal TotalPrice => Quantity * UnitPrice;

    public Guid? CategoryId { get; private set; }

    public ReceiptItem(Guid id, Guid receiptId, string name, decimal quantity, decimal unitPrice, Guid? categoryId = null)
    {
        Id = id;
        ReceiptId = receiptId;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CategoryId = categoryId;
    }

    public void AssignCategory(Guid categoryId)
    {
        CategoryId = categoryId;
    }
}
