using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;

namespace ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;

/// <summary>
/// Единая точка записи журнала решений категоризации (ADR-033 п. 6, решение C1). Вызывается
/// строго после фиксации пользовательского сохранения, одним батчем на запрос.
/// </summary>
public interface ICategorizationDecisionRecorder
{
    /// <summary>
    /// Валидирует черновики, проставляет серверное время и пишет события одним
    /// <c>AddRangeAsync</c>. Невалидные черновики отбрасываются с <c>Warning</c>.
    /// <para>
    /// Контракт (C1/NFR-3): метод <b>не бросает исключений ни при каких обстоятельствах</b> —
    /// включая сбой репозитория и отмену <paramref name="cancellationToken"/>. Ошибки
    /// логируются как <c>Error</c>; вызывающий save-путь не обрабатывает результат записи
    /// и не меняет из-за неё ответ пользователю.
    /// </para>
    /// </summary>
    /// <param name="drafts">
    /// Черновики событий («предложено → принято»); пустой набор — валидный вызов без записи.
    /// </param>
    /// <param name="cancellationToken">Токен запроса; его отмена не приводит к исключению из метода.</param>
    Task RecordAsync(
        IReadOnlyCollection<CategorizationDecisionDraft> drafts,
        CancellationToken cancellationToken = default);
}
