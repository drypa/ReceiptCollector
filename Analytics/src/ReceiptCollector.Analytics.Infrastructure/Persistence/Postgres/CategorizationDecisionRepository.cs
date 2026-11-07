using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Репозиторий журнала категоризации (ADR-033 п. 2, решение B1): только запись сырых событий.
/// Батч вставляется одним <c>SaveChangesAsync</c>, без отдельной транзакции (C1).
/// </summary>
internal sealed class CategorizationDecisionRepository : ICategorizationDecisionRepository
{
    private readonly ReceiptDbContext _dbContext;

    public CategorizationDecisionRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddRangeAsync(
        IReadOnlyCollection<CategorizationDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        if (decisions.Count == 0)
        {
            return;
        }

        _dbContext.CategorizationDecisions.AddRange(
            decisions.Select(CategorizationDecisionEntity.Create));

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
