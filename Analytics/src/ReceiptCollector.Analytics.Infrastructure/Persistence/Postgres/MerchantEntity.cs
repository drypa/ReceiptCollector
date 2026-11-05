using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class MerchantEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MerchantCategory Category { get; set; }
    public string? Address { get; set; }
    public string? Inn { get; set; }

    /// <summary>
    /// Сырой код <see cref="CommodityCategory"/> — товарная категория по умолчанию (ADR-032, A1).
    /// 0 (Undefined) запрещён CHECK в миграции; чтение в домен — только через безопасное
    /// <see cref="MapToDomain"/> (значение вне справочника → «не задана»).
    /// </summary>
    public int? DefaultCommodityCategoryId { get; set; }

    internal static MerchantEntity Create(Merchant merchant)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        return new MerchantEntity
        {
            Id = merchant.Id,
            Name = merchant.Name,
            Category = merchant.Category,
            Address = merchant.Address,
            Inn = merchant.Inn,
            DefaultCommodityCategoryId = merchant.DefaultCommodityCategory is CommodityCategory value ? (int)value : null
        };
    }

    internal Merchant MapToDomain()
    {
        // Безопасное чтение (ADR-032, A1; прецедент PrecategorizedCommodity, ADR-027 D11):
        // код вне справочника CommodityCategory или 0 (Undefined) трактуется доменом
        // как «дефолт не задан» — edge case «категория удалена из справочника».
        CommodityCategory? defaultCommodityCategory =
            DefaultCommodityCategoryId is int code
            && code != 0
            && Enum.IsDefined(typeof(CommodityCategory), code)
                ? (CommodityCategory)code
                : null;

        return new Merchant(Id, Name, Category, Address, Inn, defaultCommodityCategory);
    }
}
