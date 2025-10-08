namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public sealed class Merchant
{
    public int Id { get; }
    public string Name { get; }
    public IReadOnlyCollection<Place> Places => _places;
    private readonly List<Place> _places = new();
}