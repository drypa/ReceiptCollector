using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Бизнес-логика админского workflow предкатегоризации (ADR-027 п.4/п.7, FR-5, FR-6).
/// Работает через <see cref="IPrecategorizedCommodityRepository"/> — единственную точку
/// глобального доступа; сервис сам <c>IgnoreQueryFilters</c> не использует (NFR-5).
/// </summary>
public interface IPrecategorizedCommodityService
{
    /// <summary>Постраничный список только <c>proposed</c> строк с поиском и общим количеством.</summary>
    Task<(IReadOnlyCollection<PrecategorizedCommodityDto> Items, int Total)> GetPageAsync(
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение предложенной категории без применения к товарам (FR-5.2, UC-3).
    /// <c>applied</c> → <see cref="PrecategorizedOperationOutcome.Conflict"/>,
    /// нет строки → <see cref="PrecategorizedOperationOutcome.NotFound"/>.
    /// </summary>
    Task<PrecategorizedOperationOutcome> UpdateCategoryAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждение: транзакция репозитория (D10) и <c>TryAdd</c> в in-memory кэш
    /// строго после фиксации транзакции (FR-6.4).
    /// <para>
    /// После успешного применения (outcome <see cref="PrecategorizedOperationOutcome.Applied"/>)
    /// — дополнительно одно событие журнала «предложено → принято» (ADR-033 п. 5, решение D1):
    /// предложено = исходное предложение строки, принято = применённая категория. Единичный
    /// отказ (<see cref="PrecategorizedOperationOutcome.NotFound"/> /
    /// <see cref="PrecategorizedOperationOutcome.Conflict"/>) событий не даёт; при
    /// <c>updatedCommodities = 0</c> событие пишется — решение состоялось. Сбой журнала не влияет
    /// на результат и ответ (NFR-3).
    /// </para>
    /// </summary>
    /// <param name="acceptedByUserId">
    /// Принявший решение — <c>UserContext.UserId</c> текущего авторизованного администратора
    /// (серверное значение, ADR-033 F1), а не данные клиента.
    /// </param>
    Task<ConfirmPrecategorizedCommodityResult> ConfirmAsync(
        Guid id,
        Guid acceptedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Отклонение — физическое удаление <c>proposed</c>-строки (C4, FR-5.4).</summary>
    Task<PrecategorizedOperationOutcome> DeclineAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовое подтверждение списка строк очереди (ADR-036 §1.1, решения A3/B2/C1/D1/E1):
    /// вся коллекция обрабатывается <b>одной транзакцией</b> репозитория
    /// (<see cref="IPrecategorizedCommodityRepository.ConfirmManyAsync"/>), логический конфликт —
    /// результат строки, а не ошибка: <c>applied</c>-строка → <c>skipped/already-applied</c>,
    /// отсутствующая строка → <c>skipped/not-found</c> (см. <see cref="BulkSkipReasons"/>);
    /// технический сбой (исключение/таймаут/отмена) пробрасывается — эндпоинт отдаёт <c>500</c>,
    /// ни одна строка не применена.
    /// <para>
    /// Строго после commit: <c>TryAdd</c> в in-memory кэш по каждой <c>applied</c>-строке
    /// (ADR-019, first-wins/no-op) и <b>один</b> вызов журнала решений всеми draft'ами
    /// применённых строк (ADR-033, E1) с <see cref="CancellationToken.None"/> — данные уже
    /// зафиксированы, уход администратора со страницы не должен уничтожать события. Сбой журнала
    /// не влияет на счётчики, результат и кэш (NFR-3). Пропущенные строки событий не дают.
    /// </para>
    /// <para>
    /// <paramref name="ids"/> пуст — нулевой результат без обращения к репозиторию (защита:
    /// валидация запроса — ответственность эндпоинта, ADR-036 §1.3).
    /// </para>
    /// </summary>
    /// <param name="acceptedByUserId">
    /// Принявший решение — <c>UserContext.UserId</c> текущего авторизованного администратора
    /// (серверное значение, ADR-033 F1), а не данные клиента.
    /// </param>
    Task<BulkConfirmResult> ConfirmManyAsync(
        IReadOnlyCollection<Guid> ids,
        Guid acceptedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовое отклонение списка строк очереди (ADR-036 §1.2): та же одна транзакция и та же
    /// классификация, что у <see cref="ConfirmManyAsync"/>; <c>Applied</c> → <c>declined</c>,
    /// <c>Conflict</c>/<c>NotFound</c> → <c>skipped</c> с причиной. Кэш и журнал решений
    /// <b>не затрагиваются</b>: отклонение — не точка фиксации решения (ADR-027, C4).
    /// Пустой <paramref name="ids"/> — нулевой результат без обращения к репозиторию.
    /// </summary>
    Task<BulkDeclineResult> DeclineManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
