namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public class Place
{
    public int Id { get; }
    public Guid MerchantId { get; }
    public string Address { get; }
    public string Inn { get; }
}