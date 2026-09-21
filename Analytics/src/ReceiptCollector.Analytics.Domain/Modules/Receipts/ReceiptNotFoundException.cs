namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class ReceiptNotFoundException : Exception
{
    public ReceiptNotFoundException(Guid receiptId)
        : base($"Receipt with id '{receiptId}' not found.")
    {
    }
}