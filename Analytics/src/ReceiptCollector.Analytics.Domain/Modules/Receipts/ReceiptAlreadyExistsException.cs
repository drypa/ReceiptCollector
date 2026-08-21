namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public sealed class ReceiptAlreadyExistsException : Exception
{
    public ReceiptAlreadyExistsException(Guid receiptId)
        : base($"Receipt with id '{receiptId}' already exists.")
    {
        ReceiptId = receiptId;
    }

    public Guid ReceiptId { get; }
}
