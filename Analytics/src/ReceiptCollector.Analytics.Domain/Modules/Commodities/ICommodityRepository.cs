namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public interface ICommodityRepository
{
    Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Присваивает категорию товару (сбрасывает категорию при <paramref name="category"/> == null).
    /// </summary>
    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory? category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает товары из списка <paramref name="ids"/>, принадлежащие пользователю
    /// <paramref name="userId"/> (ADR-028 п. 9). Неизвестные/чужие идентификаторы просто
    /// отсутствуют в результате — исключения нет (чтение терпит устаревшую страницу).
    /// <para>
    /// <b>Owner-фильтр обязателен явно</b> (<c>c.Receipt.UserId == userId</c>): глобальный
    /// query-filter на <c>Receipts</c> в <c>CommodityRepository</c> не активен —
    /// <c>ReceiptDbContext.SetCurrentUser</c> вызывается только в <c>ReceiptRepository</c>,
    /// поэтому полагаться на фильтр нельзя.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Commodity>> GetByOwnerAsync(
        Guid userId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Пакетно присваивает категории товарам пользователя <paramref name="userId"/> (ADR-028 п. 1.2, 9):
    /// одна транзакция (один <c>SaveChangesAsync</c>), assign-only (сброс категории этим методом
    /// не поддерживается), возвращает число строк, у которых сохранённое значение **реально
    /// изменилось** (old != new).
    /// <para>
    /// Если хотя бы один товар из <paramref name="changes"/> не найден среди товаров пользователя,
    /// выбрасывается <see cref="CommoditiesNotAccessibleException"/> **до** любых мутаций —
    /// в БД ничего не записывается.
    /// </para>
    /// <para>
    /// <b>Owner-фильтр обязателен явно</b> (<c>c.Receipt.UserId == userId</c>): глобальный
    /// query-filter на <c>Receipts</c> в <c>CommodityRepository</c> не активен (см.
    /// <see cref="GetByOwnerAsync"/>).
    /// </para>
    /// </summary>
    Task<int> UpdateCategoriesAsync(
        Guid userId,
        IReadOnlyDictionary<Guid, CommodityCategory> changes,
        CancellationToken cancellationToken = default);
}
