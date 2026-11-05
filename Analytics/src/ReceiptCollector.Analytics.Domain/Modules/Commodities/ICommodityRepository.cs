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

    /// <summary>
    /// Применяет товарную категорию по умолчанию магазина ко всем позициям чека
    /// <paramref name="receiptId"/> <b>без категории</b> (<c>category_id IS NULL OR = 0</c>) —
    /// автоприменение при импорте чека (ADR-032, C1/FR-M2).
    /// <para>
    /// Одна транзакция БД: выборка наименований позиций без категории и <c>UPDATE</c>
    /// (<c>CategoryId</c>, <c>CategoryName</c>, <c>category_source = 'merchant-default'</c>) —
    /// единственная точка записи этого маркера источника (B1/FR-M6). После фиксации записи —
    /// <c>TryAdd</c> в in-memory кэш по нормализованным именам обновленных строк (F1, first-wins
    /// и no-op при «заморозке» — семантика ADR 019); при нуле обновленных строк — ни одного
    /// <c>TryAdd</c>.
    /// </para>
    /// <para>
    /// Позиции с содержательной категорией (≠ NULL, ≠ 0) не трогаются (FR-M5); повторный вызов
    /// идемпотентен — 0 строк, <c>category_source</c>/<c>category_name</c> не меняются.
    /// JOIN с receipts не требуется (у позиции есть <c>receipt_id</c>), owner query-filter на
    /// операцию не влияет.
    /// </para>
    /// </summary>
    /// <returns>Число обновленных строк.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="category"/> = Undefined или код вне справочника — дефолтом может быть
    /// только валидная содержательная категория (ADR-032, A1).
    /// </exception>
    Task<int> ApplyMerchantDefaultForReceiptAsync(
        Guid receiptId,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Дожим: применяет товарную категорию по умолчанию ко всем позициям <b>без категории</b>
    /// всех чеков магазина <paramref name="merchantId"/> (ADR-032, E1/FR-M3) — операция кнопки
    /// «категоризировать существующие».
    /// <para>
    /// Тот же предикат и тот же набор setters, что у
    /// <see cref="ApplyMerchantDefaultForReceiptAsync"/>, но карточка <c>commodities JOIN receipts</c>
    /// с условием <c>receipts.merchant_id = :merchantId</c> и <c>IgnoreQueryFilters()</c>:
    /// <b>доступ вне owner-scoping намеренный — дожим по магазину обрабатывает позиции всех
    /// пользователей (ADR-032, E1)</b>; маркер в стиле ADR-027 D8/NFR-5.
    /// </para>
    /// <para>
    /// Одна транзакция БД; <c>TryAdd</c> в кэш по нормализованным именам обновленных строк —
    /// только после фиксации записи (F1); транзакцию на уровне сервиса-дозима дублировать не
    /// нужно (ADR-032 п. 4). Идемпотентность, неприкосновенность содержательных категорий (FR-M5)
    /// и остальные гарантии — как у <see cref="ApplyMerchantDefaultForReceiptAsync"/>.
    /// </para>
    /// </summary>
    /// <returns>Число обновленных строк (0 — все позиции уже категоризированы, это успех).</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="category"/> = Undefined или код вне справочника — дефолтом может быть
    /// только валидная содержательная категория (ADR-032, A1).
    /// </exception>
    Task<int> ApplyMerchantDefaultForMerchantAsync(
        Guid merchantId,
        CommodityCategory category,
        CancellationToken cancellationToken = default);
}
