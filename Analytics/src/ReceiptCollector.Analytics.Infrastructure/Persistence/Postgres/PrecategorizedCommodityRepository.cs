using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Репозиторий очереди предкатегоризации (ADR-027 п.2–п.4). Здесь и только здесь
/// допускается глобальный доступ к товарам всех пользователей: запросы-корни идут через
/// <c>Receipts.IgnoreQueryFilters()</c> (или raw SQL) с комментарием о намеренности (D8, NFR-5).
/// </summary>
internal sealed class PrecategorizedCommodityRepository : IPrecategorizedCommodityRepository
{
    /// <summary>Максимальная длина наименования в схеме (commodities.name varchar(256), FR-4.7).</summary>
    private const int MaxNameLength = 256;

    private readonly ReceiptDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrecategorizedCommodityRepository> _logger;

    public PrecategorizedCommodityRepository(
        ReceiptDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<PrecategorizedCommodityRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyCollection<CandidateCursor>> GetCandidatesAsync(
        CandidateCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // DISTINCT-выборка пар (name, merchant_id) по товарам ВСЕХ пользователей.
        // Доступ вне owner-scoping намеренный: фоновая предкатегоризация обрабатывает данные
        // всех пользователей (ADR-027, D8). Прямой ссылки commodity → merchant нет, магазин
        // берётся через receipts. Курсор (D14) реализован row-comparison по (name, merchant_id):
        // сравнение Guid в EF-трансляции ненадёжно (замечание 6 плана), raw SQL предсказуем.
        const string baseSql = """
            SELECT DISTINCT c.name, r.merchant_id
            FROM commodities AS c
            INNER JOIN receipts AS r ON r.id = c.receipt_id
            WHERE (c.category_id IS NULL OR c.category_id = 0)
              AND NOT EXISTS (
                  SELECT 1
                  FROM precategorized_commodities AS existing_pair
                  WHERE existing_pair.name = c.name
                    AND existing_pair.merchant_id = r.merchant_id)
              AND NOT EXISTS (
                  SELECT 1
                  FROM precategorized_commodities AS proposed_name
                  WHERE proposed_name.name = c.name
                    AND proposed_name.status = 0)
            """;

        string sql;
        object[] parameters;

        if (after is null)
        {
            sql = $"{baseSql} ORDER BY c.name, r.merchant_id LIMIT @p0";
            parameters = [limit];
        }
        else
        {
            sql = $"""
                {baseSql}
                  AND (c.name, r.merchant_id) > (@p0::varchar, @p1::uuid)
                ORDER BY c.name, r.merchant_id
                LIMIT @p2
                """;
            parameters = [after.Name, after.MerchantId, limit];
        }

        var rows = await _dbContext.Database
            .SqlQueryRaw<CandidateRow>(sql, parameters)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new CandidateCursor(row.Name, row.MerchantId))
            .ToList();
    }

    public async Task<CommodityCategory?> GetKnownCategoryByNameAsync(
        string exactName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactName);

        // Доступ вне owner-scoping намеренный: глобальный existing-шаг фона ищет категорию
        // по точному наименованию среди товаров всех пользователей (ADR-027, D1/D8).
        // IgnoreQueryFilters — явный маркер глобального доступа (у Commodities фильтра нет).
        var categoryId = await _dbContext.Commodities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Name == exactName && c.CategoryId != null && c.CategoryId != 0)
            .OrderBy(c => c.CategoryId)
            .Select(c => c.CategoryId!.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (categoryId == 0 || !Enum.IsDefined(typeof(CommodityCategory), categoryId))
        {
            return null;
        }

        var category = (CommodityCategory)categoryId;
        return category == CommodityCategory.Undefined ? null : category;
    }

    public async Task<IReadOnlyCollection<PrecategorizedCommodity>> GetPageAsync(
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = BuildPageQuery(search);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.MerchantId,
                MerchantName = p.Merchant != null ? p.Merchant.Name : null,
                p.ProposedCategoryId,
                p.Status,
                p.CreatedAt,
                p.AppliedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new PrecategorizedCommodity(
                row.Id,
                row.Name,
                row.MerchantId,
                row.MerchantName,
                (CommodityCategory)row.ProposedCategoryId,
                row.Status,
                row.CreatedAt,
                row.AppliedAt))
            .ToList();
    }

    public Task<int> GetPageCountAsync(string? search, CancellationToken cancellationToken = default)
        => BuildPageQuery(search).CountAsync(cancellationToken);

    public async Task<PrecategorizedCommodity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.PrecategorizedCommodities
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.MerchantId,
                MerchantName = p.Merchant != null ? p.Merchant.Name : null,
                p.ProposedCategoryId,
                p.Status,
                p.CreatedAt,
                p.AppliedAt
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new PrecategorizedCommodity(
            row.Id,
            row.Name,
            row.MerchantId,
            row.MerchantName,
            (CommodityCategory)row.ProposedCategoryId,
            row.Status,
            row.CreatedAt,
            row.AppliedAt);
    }

    public async Task<PrecategorizedOperationOutcome> UpdateProposedCategoryAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureAssignableCategory(category);

        var entity = await _dbContext.PrecategorizedCommodities
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrecategorizedOperationOutcome.NotFound;
        }

        if (entity.Status == PrecategorizedCommodityStatus.Applied)
        {
            return PrecategorizedOperationOutcome.Conflict;
        }

        entity.ProposedCategoryId = (int)category;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return PrecategorizedOperationOutcome.Applied;
    }

    public async Task<bool> TryAddProposalAsync(
        CandidateCursor candidate,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureAssignableCategory(category);

        if (string.IsNullOrWhiteSpace(candidate.Name) || candidate.Name.Length > MaxNameLength)
        {
            // FR-4.7: защита от наименований, не помещающихся в схему.
            _logger.LogWarning(
                "Skipping precategorization proposal with invalid name length {NameLength} (merchant {MerchantId}).",
                candidate.Name?.Length ?? 0,
                candidate.MerchantId);
            return false;
        }

        var entity = new PrecategorizedCommodityEntity
        {
            Id = Guid.NewGuid(),
            Name = candidate.Name,
            MerchantId = candidate.MerchantId,
            ProposedCategoryId = (int)category,
            Status = PrecategorizedCommodityStatus.Proposed,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _dbContext.PrecategorizedCommodities.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // FR-4.5: unique-конфликт (name, merchant_id) — параллельная вставка, no-op.
            _dbContext.Entry(entity).State = EntityState.Detached;
            _logger.LogDebug(
                "Precategorized proposal for '{Name}' (merchant {MerchantId}) already exists; skipping.",
                candidate.Name,
                candidate.MerchantId);
            return false;
        }
    }

    public async Task<ConfirmPrecategorizedResult> ConfirmAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureAssignableCategory(category);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = await _dbContext.PrecategorizedCommodities
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return new ConfirmPrecategorizedResult(PrecategorizedOperationOutcome.NotFound, null, null, 0);
        }

        if (entity.Status == PrecategorizedCommodityStatus.Applied)
        {
            return new ConfirmPrecategorizedResult(PrecategorizedOperationOutcome.Conflict, entity.Name, null, 0);
        }

        var categoryName = CommodityCategoryHelper.GetDisplayName(category);

        // FR-6.1–6.3: товары ВСЕХ пользователей с точно таким же наименованием, только без
        // категории (NULL или Undefined). Доступ вне owner-scoping намеренный (ADR-027, D8/D10).
        var updatedCommodities = await _dbContext.Commodities
            .IgnoreQueryFilters()
            .Where(c => c.Name == entity.Name && (c.CategoryId == null || c.CategoryId == 0))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.CategoryId, (int?)category)
                    .SetProperty(c => c.CategoryName, categoryName),
                cancellationToken)
            .ConfigureAwait(false);

        entity.Status = PrecategorizedCommodityStatus.Applied;
        entity.AppliedAt = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // «Сестринские» proposed-строки с тем же точным наименованием больше не нужны (D10).
        await _dbContext.PrecategorizedCommodities
            .Where(p => p.Name == entity.Name
                        && p.Status == PrecategorizedCommodityStatus.Proposed
                        && p.Id != entity.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ConfirmPrecategorizedResult(
            PrecategorizedOperationOutcome.Applied,
            entity.Name,
            category,
            updatedCommodities);
    }

    public async Task<PrecategorizedOperationOutcome> DeleteProposedAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PrecategorizedCommodities
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return PrecategorizedOperationOutcome.NotFound;
        }

        if (entity.Status == PrecategorizedCommodityStatus.Applied)
        {
            return PrecategorizedOperationOutcome.Conflict;
        }

        _dbContext.PrecategorizedCommodities.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return PrecategorizedOperationOutcome.Applied;
    }

    public async Task<IReadOnlyDictionary<string, CommodityCategory>> GetCategoriesByExactNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        var result = new Dictionary<string, CommodityCategory>(StringComparer.Ordinal);
        if (names.Count == 0)
        {
            return result;
        }

        var nameList = names.Distinct(StringComparer.Ordinal).ToList();

        // FR-9.2: поиск по точному наименованию в любом статусе, самая свежая строка при дублях.
        var rows = await _dbContext.PrecategorizedCommodities
            .AsNoTracking()
            .Where(p => nameList.Contains(p.Name))
            .OrderByDescending(p => p.AppliedAt ?? p.CreatedAt)
            .Select(p => new { p.Name, p.ProposedCategoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (result.ContainsKey(row.Name))
            {
                continue; // уже выбрана самая свежая строка
            }

            if (!Enum.IsDefined(typeof(CommodityCategory), row.ProposedCategoryId))
            {
                continue; // значение удалено из реестра категорий (FR-9.2)
            }

            var category = (CommodityCategory)row.ProposedCategoryId;
            if (category == CommodityCategory.Undefined)
            {
                continue;
            }

            result[row.Name] = category;
        }

        return result;
    }

    private IQueryable<PrecategorizedCommodityEntity> BuildPageQuery(string? search)
    {
        var query = _dbContext.PrecategorizedCommodities
            .AsNoTracking()
            .Where(p => p.Status == PrecategorizedCommodityStatus.Proposed);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern)
                || (p.Merchant != null && EF.Functions.ILike(p.Merchant.Name, pattern)));
        }

        return query;
    }

    private static void EnsureAssignableCategory(CommodityCategory category)
    {
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            throw new ArgumentException("Category Undefined cannot be stored in the precategorization queue.", nameof(category));
        }
    }

    /// <summary>Строка raw-выборки кандидатов (пара name + merchant_id).</summary>
    private sealed class CandidateRow
    {
        public string Name { get; set; } = string.Empty;

        public Guid MerchantId { get; set; }
    }
}
