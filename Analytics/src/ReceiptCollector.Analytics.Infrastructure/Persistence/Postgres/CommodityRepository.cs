using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class CommodityRepository : ICommodityRepository
{
    private readonly ReceiptDbContext _dbContext;

    /// <summary>
    /// In-memory кэш категорий для пополнения при применении дефолта магазина (ADR-032, F1).
    /// Параметр со значением по умолчанию (<c>null</c>) — сохранение совместимости существующих
    /// тестовых фабрик репозитория (прецедент T-03: <c>CommodityCategoryResolver</c>); в production
    /// регистрируется всегда (<c>DependencyInjectionExtensions</c>, singleton). Без кэша запись в БД
    /// не меняется, пополнение просто пропускается.
    /// </summary>
    private readonly ICommodityCategoryCache? _cache;

    public CommodityRepository(ReceiptDbContext dbContext, ICommodityCategoryCache? cache = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache;
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

    /// <inheritdoc />
    public async Task<int> ApplyMerchantDefaultForReceiptAsync(
        Guid receiptId,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureAssignableDefaultCategory(category);

        // Выборка имен и UPDATE — в одной транзакции: вынесение TryAdd на уровень сервиса
        // дало бы гонку «имена прочитаны до, UPDATE после чужой записи» (ADR-032, amend,
        // открытый вопрос 1; прецедент ADR-027 п. 4). JOIN с receipts не нужен — у позиции
        // есть receipt_id, owner query-filter операции не затрагивает (ADR-032, C1).
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await _dbContext.Commodities
            .Where(c => c.ReceiptId == receiptId && (c.CategoryId == null || c.CategoryId == 0))
            .Select(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // FR-M5/D11: обновляются только позиции без категории (NULL или 0); содержательные
        // категории не перезаписываются. CategorySource — единственная точка записи
        // 'merchant-default' (ADR-032, B1); существующие методы колонку не пишут.
        var updated = await _dbContext.Commodities
            .Where(c => c.ReceiptId == receiptId && (c.CategoryId == null || c.CategoryId == 0))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.CategoryId, (int?)category)
                    .SetProperty(c => c.CategoryName, CommodityCategoryHelper.GetDisplayName(category))
                    .SetProperty(c => c.CategorySource, CategorizationSourceNames.MerchantDefault),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // F1: кэш пополняется строго ПОСЛЕ фиксации записи (прецедент ConfirmAsync +
        // PrecategorizedCommodityService, ADR-027 п. 4); при updated = 0 — ни одного TryAdd.
        if (updated > 0)
        {
            AddNamesToCache(names, category);
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task<int> ApplyMerchantDefaultForMerchantAsync(
        Guid merchantId,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureAssignableDefaultCategory(category);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Доступ вне owner-scoping намеренный: дожим по магазину обрабатывает позиции всех пользователей (ADR-032, E1).
        // Query-filter объявлен на Receipts и навигация на Receipt его наследует, поэтому
        // IgnoreQueryFilters() — обязательный явный маркер (стиль ADR-027 D8/NFR-5). Прямой ссылки
        // commodity → merchant нет, магазин берётся через receipts.
        var names = await _dbContext.Commodities
            .IgnoreQueryFilters()
            .Where(c => c.Receipt.MerchantId == merchantId && (c.CategoryId == null || c.CategoryId == 0))
            .Select(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Тот же предикат и тот же набор setters, что у операций импорта (ADR-032 п. 4).
        var updated = await _dbContext.Commodities
            .IgnoreQueryFilters()
            .Where(c => c.Receipt.MerchantId == merchantId && (c.CategoryId == null || c.CategoryId == 0))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.CategoryId, (int?)category)
                    .SetProperty(c => c.CategoryName, CommodityCategoryHelper.GetDisplayName(category))
                    .SetProperty(c => c.CategorySource, CategorizationSourceNames.MerchantDefault),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // F1: TryAdd — после commit; при updated = 0 — ни одного вызова.
        if (updated > 0)
        {
            AddNamesToCache(names, category);
        }

        return updated;
    }

    /// <summary>
    /// Пополнение in-memory кэша по нормализованным именам обновленных строк (ADR-032, F1).
    /// Здесь только перебор уникальных исходных имен; нормализация, first-wins (ADR 019 FR-1.4)
    /// и no-op при «заморозке» кэша (FR-1.5) — семантика самого кэша.
    /// </summary>
    private void AddNamesToCache(List<string> names, CommodityCategory category)
    {
        if (_cache is null)
        {
            return;
        }

        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            _cache.TryAdd(CommodityNameNormalizer.NormalizeName(name), category);
        }
    }

    /// <summary>
    /// Дефолтом магазина может быть только валидная содержательная категория (ADR-032, A1:
    /// Undefined запрещен CHECK'ом на уровне схемы, невалидный код читается как «дефолта нет»).
    /// Запись сюда такого кода — ошибка вызывающего кода, а не рядовое состояние.
    /// </summary>
    private static void EnsureAssignableDefaultCategory(CommodityCategory category)
    {
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            throw new ArgumentException(
                "Undefined and codes outside the commodity category registry cannot be applied as a merchant default.",
                nameof(category));
        }
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
