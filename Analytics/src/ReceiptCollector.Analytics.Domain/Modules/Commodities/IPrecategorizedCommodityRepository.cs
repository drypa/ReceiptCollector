namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Доступ к очереди предкатегоризации (<c>precategorized_commodities</c>, ADR-027 п.2–п.4).
/// Единственная точка глобального (вне owner-scoping) доступа к товарам всех пользователей:
/// реализация обязана использовать <c>IgnoreQueryFilters()</c> с комментарием о намеренности (D8, NFR-5).
/// </summary>
public interface IPrecategorizedCommodityRepository
{
    /// <summary>
    /// Кандидаты фоновой сессии (FR-3.1–3.5): DISTINCT пары <c>(name, merchant_id)</c> по товарам
    /// всех пользователей с <c>category_id IS NULL OR = 0</c>, за исключением пар, уже присутствующих
    /// в очереди в любом статусе (FR-3.2 — приоритет выше FR-3.3), и наименований, у которых есть
    /// <c>proposed</c>-строка у другого магазина (FR-3.4). Порядок — <c>(name, merchant_id)</c>,
    /// продолжение — после <paramref name="after"/> (D14).
    /// </summary>
    Task<IReadOnlyCollection<CandidateCursor>> GetCandidatesAsync(
        CandidateCursor? after,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Глобальный existing-шаг фона (D1/FR-3.3): первая содержательная категория
    /// (<c>category_id IS NOT NULL AND &lt;&gt; 0</c>) среди товаров всех пользователей
    /// по точному наименованию. <c>null</c> — категории нет.
    /// </summary>
    Task<CommodityCategory?> GetKnownCategoryByNameAsync(string exactName, CancellationToken cancellationToken = default);

    /// <summary>Страница очереди только в статусе <c>proposed</c> (FR-5.1), сортировка <c>created_at DESC</c>.</summary>
    Task<IReadOnlyCollection<PrecategorizedCommodity>> GetPageAsync(
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>Общее число строк в статусе <c>proposed</c> с учётом поиска (X-Total-Count).</summary>
    Task<int> GetPageCountAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>Строка очереди по идентификатору (любой статус).</summary>
    Task<PrecategorizedCommodity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение предложенной категории (FR-5.2). Товары не затрагиваются (UC-3).
    /// <c>applied</c> → <see cref="PrecategorizedOperationOutcome.Conflict"/>, нет строки → NotFound.
    /// </summary>
    Task<PrecategorizedOperationOutcome> UpdateProposedCategoryAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Вставка <c>proposed</c>-строки. Unique-конфликт <c>(name, merchant_id)</c> не пробрасывается
    /// (FR-4.5): возвращается <c>false</c> (no-op), сессия продолжается.
    /// </summary>
    Task<bool> TryAddProposalAsync(
        CandidateCursor candidate,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждение предложения одной транзакцией (D10, FR-6.1–6.5): обновление
    /// <c>commodities</c> всех пользователей по точному имени (только без категории),
    /// перевод строки в <c>applied</c>, удаление «сестринских» <c>proposed</c>-строк.
    /// Кэш здесь не трогается — это делает сервис после фиксации транзакции.
    /// </summary>
    Task<ConfirmPrecategorizedResult> ConfirmAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклонение предложения — физическое удаление только <c>proposed</c>-строки (C4, FR-5.4).
    /// <c>applied</c> → <see cref="PrecategorizedOperationOutcome.Conflict"/>.
    /// </summary>
    Task<PrecategorizedOperationOutcome> DeleteProposedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-поиск по очереди для ручного suggest (FR-9.2/9.3): словарь
    /// <c>точное наименование → категория</c> по строкам в любом статусе; при дублях берётся
    /// самая свежая (<c>applied_at</c>/<c>created_at</c>); невалидные значения enum отбрасываются.
    /// </summary>
    Task<IReadOnlyDictionary<string, CommodityCategory>> GetCategoriesByExactNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);
}
