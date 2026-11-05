using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class MerchantRepository : IMerchantRepository
{
    private readonly ReceiptDbContext _dbContext;

    public MerchantRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(Merchant merchant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        MerchantEntity? entity = null;

        if (!string.IsNullOrWhiteSpace(merchant.Inn))
        {
            entity = await _dbContext.Merchants
                .FirstOrDefaultAsync(m => m.Inn == merchant.Inn, cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            entity = await _dbContext.Merchants
                .FirstOrDefaultAsync(m => m.Id == merchant.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        if (entity is null)
        {
            entity = MerchantEntity.Create(merchant);
            await _dbContext.Merchants.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            entity.Name = merchant.Name;
            entity.Category = merchant.Category;
            entity.Address = merchant.Address;
            entity.Inn = merchant.Inn;
            // Дефолтная товарная категория сохраняется тем же «репозиторным путём редактирования»
            // (AddAsync — путь UpdateMerchantName/ADR 011, его же использует эндпоинт дефолта, ADR-032 п. 6).
            // Вызовы AddAsync передают либо новый магазин (null), либо загруженный из БД — значение round-trip-ится.
            entity.DefaultCommodityCategoryId = merchant.DefaultCommodityCategory is CommodityCategory value ? (int)value : null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Merchant?> GetByIdAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken)
            .ConfigureAwait(false);

        return entity?.MapToDomain();
    }

    public async Task<Merchant?> GetByInnAsync(string inn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inn))
        {
            throw new ArgumentException("Merchant INN must be provided.", nameof(inn));
        }

        var entity = await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Inn == inn, cancellationToken)
            .ConfigureAwait(false);

        return entity?.MapToDomain();
    }

    public async Task<IReadOnlyCollection<Merchant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.Merchants
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(entity => entity.MapToDomain()).ToList();
    }

    public async Task UpdateCategoryAsync(Guid merchantId, MerchantCategory category, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Merchants
            .FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Merchant with id '{merchantId}' not found.");
        }

        entity.Category = category;

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultCommodityCategoriesAsync(
        IReadOnlyCollection<Guid> merchantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(merchantIds);

        var result = new Dictionary<Guid, CommodityCategory?>();
        if (merchantIds.Count == 0)
        {
            return result;
        }

        var idList = merchantIds.Distinct().ToList();

        // Batch для resolver'а (ADR-032, D1): один запрос на порцию, без JOIN с receipts —
        // вызывающий слой сам сопоставляет позиции с merchantId.
        var rows = await _dbContext.Merchants
            .AsNoTracking()
            .Where(m => idList.Contains(m.Id) && m.DefaultCommodityCategoryId != null)
            .Select(m => new { m.Id, m.DefaultCommodityCategoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            // Только валидные ненулевые коды справочника (то же безопасное чтение, что в
            // MerchantEntity.MapToDomain): магазины с NULL/0/устаревшим кодом в словарь не попадают.
            if (row.DefaultCommodityCategoryId is not int code
                || code == 0
                || !Enum.IsDefined(typeof(CommodityCategory), code))
            {
                continue;
            }

            result[row.Id] = (CommodityCategory)code;
        }

        return result;
    }
}