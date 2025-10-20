namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public sealed class Merchant
{
    public Guid Id { get; }
    public string Name { get; }
    public MerchantCategory Category { get; private set; } = MerchantCategory.Undefined;
    public string Address { get; }
    public string Inn { get; }

}