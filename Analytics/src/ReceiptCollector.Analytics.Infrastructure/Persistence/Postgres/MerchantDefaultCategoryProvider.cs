using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Batch-провайдер товарных категорий по умолчанию магазинов чеков (ADR-032, D1):
/// <c>receipts JOIN merchants</c> c <c>AsNoTracking</c>,
/// <c>WHERE receipts.id IN (...)</c> — ровно один запрос на порцию (N+1 запрещён, стиль FR-9.3 ADR-027).
/// Только чтение: ничего не сохраняет (suggest read-only, ADR-032 п. 5, решение F).
/// Обработку ошибок чтения несёт вызывающая сторона (resolver: Warning + деградация).
/// </summary>
internal sealed class MerchantDefaultCategoryProvider : IMerchantDefaultCategoryProvider
{
    private readonly ReceiptDbContext _dbContext;

    public MerchantDefaultCategoryProvider(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultsByReceiptIdsAsync(
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiptIds);

        var result = new Dictionary<Guid, CommodityCategory?>();
        if (receiptIds.Count == 0)
        {
            return result;
        }

        var idList = receiptIds.Distinct().ToList();

        // JOIN receipts → merchants по существующей навигации (receipts.merchant_id, ADR-032 п. 5):
        // один batch-запрос на порцию. Query-filter Receipts (owner-scoping) не обходится:
        // позиции конвейера всегда принадлежат текущему пользователю suggest-запроса.
        var rows = await _dbContext.Receipts
            .AsNoTracking()
            .Where(r => idList.Contains(r.Id))
            .Select(r => new { r.Id, r.Merchant.DefaultCommodityCategoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            // Безопасное чтение (ADR-032, A1; тот же фильтр, что MerchantEntity.MapToDomain и
            // MerchantRepository.GetDefaultCommodityCategoriesAsync): NULL/0/устаревший код
            // справочника → null, «дефолта нет» — consumer промахивается шаг и идёт дальше (FR-M8).
            if (row.DefaultCommodityCategoryId is int code
                && code != 0
                && Enum.IsDefined(typeof(CommodityCategory), code))
            {
                result[row.Id] = (CommodityCategory)code;
            }
            else
            {
                result[row.Id] = null;
            }
        }

        return result;
    }
}
