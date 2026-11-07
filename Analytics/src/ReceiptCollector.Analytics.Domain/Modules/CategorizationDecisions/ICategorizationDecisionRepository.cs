namespace ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

/// <summary>
/// Доступ к журналу решений категоризации (<c>categorization_decisions</c>, ADR-033 п. 2).
/// На этом шаге — только запись сырых событий; чтение появится в T-07.
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
}
