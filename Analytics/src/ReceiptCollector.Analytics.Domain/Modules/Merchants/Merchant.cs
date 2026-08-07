namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public sealed class Merchant
{
    public Merchant(Guid id, string name, MerchantCategory category = MerchantCategory.Undefined, string? address = null, string? inn = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Merchant name must be provided.", nameof(name));
        }

        Id = id;
        Name = name;
        Category = category;
        Address = address;
        Inn = inn;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public MerchantCategory Category { get; private set; }

    public string? Address { get; private set; }

    public string? Inn { get; private set; }

    public void UpdateCategory(MerchantCategory category)
    {
        Category = category;
    }

    public void UpdateName(string name)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Merchant name must be provided.", nameof(name));
        }

        if (name.Length > 256)
        {
            throw new ArgumentException("Merchant name must be at most 256 characters.", nameof(name));
        }

        Name = name;
    }
}