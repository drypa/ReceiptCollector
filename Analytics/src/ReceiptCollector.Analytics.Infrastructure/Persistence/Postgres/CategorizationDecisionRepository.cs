using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Репозиторий журнала категоризации (ADR-033 п. 2, решения B1/C1/E1). Запись — батч одним
/// <c>SaveChangesAsync</c>, без отдельной транзакции (C1). Чтение — серверная выборка уникальных
/// сочетаний: фильтры к событиям → дедуп anti-join → порядок → <c>Skip/Take</c>;
/// <c>Total</c> — <c>Count()</c> того же anti-join. Дедуп выполняется на уровне EF-запроса (SQL
/// NOT EXISTS), а не в памяти приложения после выборки (критерий приёмки T-07).
/// Журнал глобальный (query-filter на сущности нет), доступ — только через admin-API (T-08).
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

    public async Task<IReadOnlyCollection<CategorizationDecision>> GetCombinationsAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);

        var rows = await BuildPageQuery(query)
            .Select(d => new
            {
                d.Id,
                d.CommodityName,
                d.NormalizedName,
                d.SuggestionSource,
                d.SuggestedCategoryId,
                d.FinalCategoryId,
                d.AcceptedBy,
                d.AcceptedByAdmin,
                d.CreatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new CategorizationDecision(
                row.Id,
                row.CommodityName,
                row.NormalizedName,
                row.SuggestionSource,
                // Числовой код сохраняется как есть, включая коды вне справочника: отображаемое
                // имя резолвит read-сервис (безопасное чтение, ADR-033 п. 1).
                (CommodityCategory)row.SuggestedCategoryId,
                (CommodityCategory)row.FinalCategoryId,
                row.AcceptedBy,
                row.AcceptedByAdmin,
                row.CreatedAt))
            .ToList();
    }

    public Task<int> CountCombinationsAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // X-Total-Count — число уникальных сочетаний под фильтром (FR-7): Count() того же
        // anti-join, что и страница, поэтому пагинация и счётчик не «расходятся».
        return BuildDeduplicatedQuery(query).CountAsync(cancellationToken);
    }

    /// <summary>
    /// Дедуп-выборка + порядок <c>created_at DESC, id DESC</c> + <c>Skip/Take</c>.
    /// </summary>
    internal IQueryable<CategorizationDecisionEntity> BuildPageQuery(CategorizationDecisionQuery query)
        => BuildDeduplicatedQuery(query)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Skip(query.Offset)
            .Take(query.Limit);

    /// <summary>
    /// Фильтры к событиям (в порядке onlyMismatch → source → suggested → final → search →
    /// from → to), затем дедуп E1: событие остаётся, только если нет более свежего события того же
    /// сочетания <c>(commodity_name, suggestion_source, suggested_category_id, final_category_id)</c>.
    /// Tie-break «свежести» — <c>(created_at, id)</c> row-comparison; сравнение <c>Guid</c> в
    /// Npgsql — известный риск (прецедент-предупреждение ADR-027), подтверждён контейнерными
    /// тестами на реальном PostgreSQL (<c>CategorizationDecisionRepositoryTests</c>): трансляция
    /// даёт NOT EXISTS с <c>c0.id &gt; c.id</c>. Детерминированность порядка гарантирована
    /// <b>внутри одного движка</b>: в проде и anti-join, и <c>ORDER BY</c> исполняет PostgreSQL,
    /// поэтому «кто принял» и порядок строк противоречить друг другу не могут. Эквивалентность
    /// порядка <c>Guid</c> в .NET (<c>Guid.CompareTo</c>) и в PostgreSQL не предполагается и не
    /// требуется — сравнения не пересекают движки.
    /// Anti-join коррелирован по отфильтрованному набору: период суживает не только выдачу, но и
    /// «последнее» событие сочетания (ADR-033 «Явно зафиксированные семантики»: фильтр → дедуп).
    /// Остальные фильтры — функции колонок ключа сочетания, поэтому повторять их в подзапросе
    /// семантически безразлично, но они остаются частью выражения автоматически.
    /// </summary>
    internal IQueryable<CategorizationDecisionEntity> BuildDeduplicatedQuery(CategorizationDecisionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var events = ApplyEventFilters(query);

        return events.Where(d => !events.Any(n =>
            n.CommodityName == d.CommodityName
            && n.SuggestionSource == d.SuggestionSource
            && n.SuggestedCategoryId == d.SuggestedCategoryId
            && n.FinalCategoryId == d.FinalCategoryId
            && (n.CreatedAt > d.CreatedAt
                || (n.CreatedAt == d.CreatedAt && n.Id > d.Id))));
    }

    private IQueryable<CategorizationDecisionEntity> ApplyEventFilters(CategorizationDecisionQuery query)
    {
        var events = _dbContext.CategorizationDecisions.AsNoTracking();

        if (query.OnlyMismatch)
        {
            events = events.Where(d => d.SuggestedCategoryId != d.FinalCategoryId);
        }

        // Wire-имя источника — точное сравнение (Ordinal в SQL); невалидные имена отсекает T-08 (400),
        // репозиторий на неизвестный источник просто ничего не найдёт.
        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source;
            events = events.Where(d => d.SuggestionSource == source);
        }

        if (query.SuggestedCategoryId is int suggestedCategoryId)
        {
            events = events.Where(d => d.SuggestedCategoryId == suggestedCategoryId);
        }

        if (query.FinalCategoryId is int finalCategoryId)
        {
            events = events.Where(d => d.FinalCategoryId == finalCategoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Поиск по исходному ИЛИ нормализованному имени, без учёта регистра (ILike, прецедент
            // очереди ADR-027); на EF InMemory ILike не поддерживается — кейсы поиска покрыты
            // контейнерными тестами на PostgreSQL.
            var pattern = $"%{query.Search.Trim()}%";
            events = events.Where(d =>
                EF.Functions.ILike(d.CommodityName, pattern)
                || EF.Functions.ILike(d.NormalizedName, pattern));
        }

        if (query.From is DateTimeOffset from)
        {
            events = events.Where(d => d.CreatedAt >= from);
        }

        if (query.To is DateTimeOffset to)
        {
            // Верхняя граница исключающая: created_at < to (ADR-033 п. 7).
            events = events.Where(d => d.CreatedAt < to);
        }

        return events;
    }
}
