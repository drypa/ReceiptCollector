using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public sealed class Merchant
{
    public Merchant(
        Guid id,
        string name,
        MerchantCategory category = MerchantCategory.Undefined,
        string? address = null,
        string? inn = null,
        CommodityCategory? defaultCommodityCategory = null)
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
        DefaultCommodityCategory = defaultCommodityCategory;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public MerchantCategory Category { get; private set; }

    public string? Address { get; private set; }

    public string? Inn { get; private set; }

    /// <summary>
    /// Товарная категория по умолчанию для позиций чеков этого магазина (ADR-032, A1);
    /// <c>null</c> = не задана. Хранится отдельно от <see cref="Category"/> магазина
    /// (разные справочники, FR-M1). Инварианта на enum в домене нет: валидация
    /// <c>Enum.IsDefined</c>/<c>!= Undefined</c> — на эндпоинте, а невалидный код из БД
    /// приводится к <c>null</c> безопасным чтением в <c>MerchantEntity.MapToDomain</c>
    /// (прецедент PrecategorizedCommodity, ADR-027 D11).
    /// </summary>
    public CommodityCategory? DefaultCommodityCategory { get; private set; }

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

    /// <summary>
    /// Устанавливает или сбрасывает (<c>null</c>) товарную категорию по умолчанию (ADR-032, A1).
    /// Домен не валидирует значение: сброс — это <c>null</c>, а «не задан» при чтении из БД
    /// обеспечивает безопасное чтение в <c>MerchantEntity.MapToDomain</c>.
    /// </summary>
    public void UpdateDefaultCommodityCategory(CommodityCategory? category)
    {
        DefaultCommodityCategory = category;
    }
}