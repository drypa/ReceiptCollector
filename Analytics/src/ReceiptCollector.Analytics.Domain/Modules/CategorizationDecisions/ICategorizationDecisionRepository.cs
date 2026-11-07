namespace ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

/// <summary>
/// Доступ к журналу решений категоризации (<c>categorization_decisions</c>, ADR-033 п. 2).
/// Запись — сырыми событиями по одному на подтверждение (FR-8); дедупликация — только на чтении (E1).
/// </summary>
public interface ICategorizationDecisionRepository
{
    /// <summary>
    /// Вставка пачки событий одним <c>SaveChangesAsync</c> (C1). Транзакция и валидация
    /// не выполняются: запись вызывается после фиксации пользовательского сохранения,
    /// валидацию черновиков выполняет recorder (T-03).
    /// </summary>
    Task AddRangeAsync(
        IReadOnlyCollection<CategorizationDecision> decisions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница уникальных сочетаний журнала (FR-7, решение E1): фильтры применяются к событиям,
    /// затем дедуп anti-join greatest-n-per-group — событие попадает в выдачу, только если нет
    /// более свежего события того же сочетания (tie-break <c>(created_at, id)</c>). Возвращаемое
    /// событие сочетания — последнее внутри отфильтрованного периода («кто принял»/<c>decidedAt</c>
    /// от него, ADR-033 «Явно зафиксированные семантики»). Порядок выдачи
    /// <c>created_at DESC, id DESC</c>; пагинация <c>Skip/Take</c> — по дедуп-выборке.
    /// </summary>
    Task<IReadOnlyCollection<CategorizationDecision>> GetCombinationsAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Число уникальных сочетаний под тем же фильтром (<c>X-Total-Count</c>, FR-7):
    /// <c>Count()</c> того же anti-join, что и <see cref="GetCombinationsAsync"/>.
    /// </summary>
    Task<int> CountCombinationsAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default);
}
