using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class MerchantEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MerchantCategory Category { get; set; }
    public string? Address { get; set; }
    public string? Inn { get; set; }

    internal static MerchantEntity Create(Merchant merchant)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        return new MerchantEntity
        {
            Id = merchant.Id,
            Name = merchant.Name,
            Category = merchant.Category,
            Address = merchant.Address,
            Inn = merchant.Inn
        };
    }

    internal Merchant MapToDomain()
    {
        return new Merchant(Id, Name, Category, Address, Inn);
    }
}
