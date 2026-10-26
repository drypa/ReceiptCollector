using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class CommodityRepository : ICommodityRepository
{
    private readonly ReceiptDbContext _dbContext;

    public CommodityRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return MapToDomain(entity);
    }

    public async Task UpdateCategoryAsync(Guid commodityId, CommodityCategory? category, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Commodity with id '{commodityId}' not found.");
        }

        if (category.HasValue)
        {
            entity.CategoryId = (int)category.Value;
            entity.CategoryName = CommodityCategoryHelper.GetDisplayName(category.Value);
        }
        else
        {
            entity.CategoryId = null;
            entity.CategoryName = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Commodity>> GetByOwnerAsync(
        Guid userId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Array.Empty<Commodity>();
        }

        var idList = ids.ToList();

        // Owner-фильтр явный: глобальный query-filter на Receipts здесь не активен
        // (SetCurrentUser вызывается только в ReceiptRepository) — ADR-028 п. 9.
        var entities = await _dbContext.Commodities
            .AsNoTracking()
            .Include(c => c.Receipt)
            .Where(c => idList.Contains(c.Id) && c.Receipt.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateCategoriesAsync(
        Guid userId,
        IReadOnlyDictionary<Guid, CommodityCategory> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return 0;
        }

        var idList = changes.Keys.ToList();

        // Owner-фильтр явный (см. GetByOwnerAsync): «свои» строки выбираются без Include —
        // достаточно навигации для фильтра по receipts.user_id.
        var entities = await _dbContext.Commodities
            .Where(c => idList.Contains(c.Id) && c.Receipt.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Проверка доступности всех id — ДО любых мутаций: ничего не записано, эндпоинт отдаст 409.
        if (entities.Count != idList.Count)
        {
            throw new CommoditiesNotAccessibleException(idList.Count - entities.Count);
        }

        var updated = 0;
        foreach (var entity in entities)
        {
            var category = changes[entity.Id];
            var newCategoryId = (int)category;

            // C9: «то же значение» изменением не считается (ни в счётчике, ни в кэше).
            if (entity.CategoryId == newCategoryId)
            {
                continue;
            }

            entity.CategoryId = newCategoryId;
            entity.CategoryName = CommodityCategoryHelper.GetDisplayName(category);
            updated++;
        }

        // Одна транзакция на пакет (ADR-028 п. 9): один SaveChangesAsync на вызов.
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return updated;
    }

    private static Commodity MapToDomain(CommodityEntity entity)
    {
        return new Commodity(
            entity.Id,
            entity.ReceiptId,
            entity.Name,
            entity.Quantity,
            entity.UnitPrice,
            entity.Nds,
            entity.NdsSum,
            entity.CategoryId.HasValue
                ? new Category(entity.CategoryId.Value, entity.CategoryName ?? "")
                : null);
    }
}
