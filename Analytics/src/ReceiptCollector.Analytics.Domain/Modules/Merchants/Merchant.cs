namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public sealed class Merchant
{
    public Guid Id { get; }
    public string Name { get; }
    public MerchantCategory Category { get; private set; } = MerchantCategory.Undefined;
    public IReadOnlyCollection<Place> Places => _places;
    private readonly List<Place> _places = new();

    public void ChangeCategory(MerchantCategory category)
    {
        Category = category;
    }
}