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
    /// Pre-check кандидатов фона по очищенному ключу (ADR-034 C1): <c>true</c> — существует
    /// строка с тем же <c>normalized_name</c> у этого магазина в любом статусе
    /// <b>или</b> любая <c>proposed</c>-строка с тем же ключом у любого магазина
    /// (FR-3.4 ADR-027 по очищенному ключу). Такой кандидат фоном пропускается —
    /// повторного предложения не будет. Ключ вычисляется в C#
    /// (<see cref="CommodityNameNormalizer"/>) и передаётся параметром: SQL-нормализации нет.
    /// </summary>
    Task<bool> HasBlockingQueueEntryAsync(
        string normalizedName,
        Guid merchantId,
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
    /// Подтверждение предложения одной транзакцией (D10, FR-6.1–6.5, ADR-034 п. 4.5):
    /// 1) загрузка строки по <paramref name="id"/> — нет строки → NotFound,
    ///    <c>applied</c> → Conflict;
    /// 2) обновление <c>commodities</c> всех пользователей по <b>точному сырому</b> имени
    ///    и только строк без категории (NFR-1 — массового применения по очищенному ключу нет);
    /// 3) перевод строки в <c>applied</c> с <c>applied_at</c>, commit.
    /// «Сестринские» <c>proposed</c>-строки не ищутся и не удаляются: глобальный partial unique
    /// по <c>normalized_name</c> гарантирует, что подтверждаемая строка — единственная
    /// <c>proposed</c> своего очищенного ключа (ADR-034 D1).
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
    /// Массовое подтверждение списка строк очереди <b>одной транзакцией</b> на весь список
    /// (ADR-036 §2.1, решения B2/C1/C3): снимок строк под <c>SELECT … FOR UPDATE</c>
    /// в порядке <c>(normalized_name, merchant_id, id)</c> → классификация
    /// <c>proposed</c>/<c>applied</c>/<c>not-found</c> → применение категории к товарам всех
    /// пользователей по точному имени (только строки без категории) → один set-based перевод
    /// очереди в <c>applied</c> → ровно один commit, последний.
    /// <para>
    /// Логический конфликт — результат строки, а не ошибка: <c>applied</c>
    /// (<see cref="PrecategorizedOperationOutcome.Conflict"/>) и отсутствующая строка
    /// (<see cref="PrecategorizedOperationOutcome.NotFound"/>) не прерывают операцию и не
    /// откатывают остальные строки. Любое исключение/таймаут/отмена до commit откатывают
    /// <b>весь</b> список: ни одна строка не остаётся применённой.
    /// </para>
    /// <para>
    /// Возвращается по одной записи на каждый переданный id (в порядке переданной коллекции);
    /// кэш и журнал решений здесь не затрагиваются — их обновляет сервис после возврата
    /// (ADR-036 §3). Поштучные <see cref="ConfirmAsync"/> и <see cref="DeleteProposedAsync"/>
    /// внутри не вызываются: они открывают собственную транзакцию (B2).
    /// </para>
    /// </summary>
    Task<IReadOnlyCollection<BulkConfirmRepositoryItem>> ConfirmManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовое отклонение списка строк очереди <b>одной транзакцией</b> на весь список
    /// (ADR-036 §2.1): тот же снимок под <c>SELECT … FOR UPDATE</c> и та же классификация, что
    /// у <see cref="ConfirmManyAsync"/>, затем одно set-based удаление классифицированных
    /// <c>proposed</c>-строк и ровно один commit, последний.
    /// <para>
    /// <c>commodities</c> не изменяются, <c>applied</c>-строки не удаляются; логические
    /// конфликты — результат строки (<c>already-applied</c>/<c>not-found</c>), технический
    /// сбой откатывает весь список.
    /// </para>
    /// </summary>
    Task<IReadOnlyCollection<BulkDeclineRepositoryItem>> DeclineManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-поиск по очереди для ручного suggest (FR-9.2/9.3, ADR-034 п. 3/п. 4.1): словарь
    /// <c>очищенное наименование (<c>normalized_name</c>) → категория</c> по строкам в любом
    /// статусе; при дублях берётся самая свежая строка (<c>applied_at ?? created_at</c>);
    /// невалидные значения enum отбрасываются. Ключ вычисляется в C#
    /// (<see cref="CommodityNameNormalizer"/>) и передаётся параметром: SQL-нормализации нет.
    /// </summary>
    Task<IReadOnlyDictionary<string, CommodityCategory>> GetCategoriesByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default);
}
