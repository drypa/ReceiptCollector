using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

namespace ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;

/// <summary>
/// Серверная выборка журнала решений категоризации (ADR-033 п. 7, FR-6/FR-7): страница уникальных
/// сочетаний с фильтрами и общим числом сочетаний. Чтение доступно только администратору — права
/// резолвит эндпоинт (T-08, <c>AdminAuthorization</c>); сервис прав не проверяет.
/// Параметр объявлен как <see cref="CategorizationDecisionQuery"/> из Domain (тот же тип принимает
/// <see cref="ICategorizationDecisionRepository"/>) — см. документацию записи.
/// </summary>
public interface ICategorizationDecisionReadService
{
    /// <summary>
    /// Страница уникальных сочетаний под фильтром <paramref name="query"/> (FR-7): выдача схлопнута
    /// дедупом, «кто принял»/<c>decidedAt</c> — от последнего события сочетания внутри периода,
    /// порядок <c>decidedAt DESC, id DESC</c>.
    /// </summary>
    /// <returns>
    /// <c>Items</c> — строки страницы (не более <see cref="CategorizationDecisionQuery.Limit"/>);
    /// <c>Total</c> — число уникальных сочетаний под тем же фильтром (<c>X-Total-Count</c>),
    /// а не число сырых событий (FR-8: хранение сырое, дедуп только на чтении).
    /// </returns>
    Task<(IReadOnlyCollection<CategorizationDecisionRow> Items, int Total)> GetPageAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default);
}
