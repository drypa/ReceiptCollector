using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public interface IMerchantRepository
{
    Task AddAsync(Merchant merchant, CancellationToken cancellationToken = default);
    Task<Merchant?> GetByIdAsync(Guid merchantId, CancellationToken cancellationToken = default);
    Task<Merchant?> GetByInnAsync(string inn, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Merchant>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Постраничное чтение магазинов с необязательным поиском (ADR-037):
    /// при непустом <paramref name="searchTerm"/> (после Trim) выбираются магазины,
    /// у которых <c>Name</c> или <c>Inn</c> содержит термин как подстроку без учёта регистра;
    /// пустой/пробельный термин эквивалентен отсутствию фильтра. Фильтр, подсчёт
    /// <c>TotalCount</c> и пагинация применяются по одному и тому же предикату; порядок
    /// детерминирован (<c>Name</c>, затем <c>Id</c>).
    /// </summary>
    Task<(IReadOnlyList<Merchant> Items, int TotalCount)> GetPageAsync(
        int limit,
        int offset,
        string? searchTerm,
        CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(Guid merchantId, MerchantCategory category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-чтение товарных категорий по умолчанию по идентификаторам магазинов (ADR-032, D1):
    /// в словарь попадают только магазины с валидным ненулевым кодом <see cref="CommodityCategory"/>;
    /// магазины без дефолта (NULL или невалидный код) в словаре отсутствуют.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultCommodityCategoriesAsync(
        IReadOnlyCollection<Guid> merchantIds,
        CancellationToken cancellationToken = default);
}