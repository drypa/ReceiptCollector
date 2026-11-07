using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;

/// <summary>
/// Read-сервис журнала решений категоризации (ADR-033 п. 7, T-07). Выборку уникальных сочетаний,
/// фильтры, пагинацию и <c>Total</c> выполняет репозиторий (anti-join E1 на уровне EF-запроса);
/// сервис добавляет только отображение: безопасные имена категорий (ADR-033 п. 1, прецедент
/// ADR-032) и имя принявшего.
/// <para>
/// Имя принявшего не денормализуется в событие (F1): batch-чтение <c>users</c> по PK страницы —
/// семантика LEFT JOIN из ADR-033 п. 7. Пользователь удалён или имя пустое →
/// <c>AcceptedByName = null</c>; <c>AcceptedById</c> и admin-флаг заполнены всегда
/// (edge case «имя недоступно»).
/// </para>
/// </summary>
internal sealed class CategorizationDecisionReadService : ICategorizationDecisionReadService
{
    /// <summary>
    /// Отображаемое имя кода категории, удалённого из справочника (enum). Числовой код при этом
    /// остаётся в строке (безопасное чтение, ADR-033 п. 1).
    /// </summary>
    private const string UnknownCategoryDisplayName = "—";

    private readonly ICategorizationDecisionRepository _repository;
    private readonly ReceiptDbContext _dbContext;

    public CategorizationDecisionReadService(
        ICategorizationDecisionRepository repository,
        ReceiptDbContext dbContext)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<(IReadOnlyCollection<CategorizationDecisionRow> Items, int Total)> GetPageAsync(
        CategorizationDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Total и страница считаются по одному anti-join (репозиторий), поэтому X-Total-Count
        // согласован с пагинацией даже при активном дедупе (FR-7).
        var total = await _repository.CountCombinationsAsync(query, cancellationToken)
            .ConfigureAwait(false);

        var decisions = await _repository.GetCombinationsAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (decisions.Count == 0)
        {
            return ([], total);
        }

        var acceptedByIds = decisions.Select(d => d.AcceptedBy).Distinct().ToList();

        // LEFT JOIN users на чтении: отсутствующая строка или пустое (в том числе пробельное)
        // имя → null (id остаётся).
        var namesById = await _dbContext.Users
            .AsNoTracking()
            .Where(u => acceptedByIds.Contains(u.Id) && !string.IsNullOrWhiteSpace(u.Name))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken)
            .ConfigureAwait(false);

        var items = decisions
            .Select(decision => new CategorizationDecisionRow(
                decision.CommodityName,
                decision.NormalizedName,
                decision.SuggestionSource,
                (int)decision.SuggestedCategory,
                GetCategoryDisplayName(decision.SuggestedCategory),
                (int)decision.FinalCategory,
                GetCategoryDisplayName(decision.FinalCategory),
                decision.AcceptedBy,
                namesById.TryGetValue(decision.AcceptedBy, out var acceptedByName)
                    ? acceptedByName
                    : null,
                decision.AcceptedByAdmin,
                decision.CreatedAt))
            .ToList();

        return (items, total);
    }

    /// <summary>
    /// Безопасное имя категории (ADR-033 п. 1): код из справочника —
    /// <c>CommodityCategoryHelper.GetDisplayName</c> (код 0 → «Не указана»), код вне справочника —
    /// «—». Helper сам по себе возвращает «Не указана» для неизвестного значения, поэтому проверка
    /// <c>Enum.IsDefined</c> выполняется до обращения к нему.
    /// </summary>
    private static string GetCategoryDisplayName(CommodityCategory category)
        => Enum.IsDefined(category)
            ? CommodityCategoryHelper.GetDisplayName(category)
            : UnknownCategoryDisplayName;
}
