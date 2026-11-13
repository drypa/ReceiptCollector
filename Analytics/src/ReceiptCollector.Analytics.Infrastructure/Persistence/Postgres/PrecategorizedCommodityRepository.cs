using System.Data;
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

    public async Task<bool> HasBlockingQueueEntryAsync(
        string normalizedName,
        Guid merchantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        // ADR-034 C1: C# pre-check кандидатов фона по очищенному ключу (SQL-нормализации нет —
        // ключ передаётся параметром). Блокирует строка с тем же normalized_name у этого
        // магазина в любом статусе ИЛИ любая proposed-строка с тем же ключом у любого
        // магазина (FR-3.4 ADR-027 по очищенному ключу). Один индексируемый запрос по
        // ix_precategorized_commodities_normalized_name (NFR-3).
        return await _dbContext.PrecategorizedCommodities
            .AsNoTracking()
            .AnyAsync(
                p => p.NormalizedName == normalizedName
                     && (p.MerchantId == merchantId || p.Status == PrecategorizedCommodityStatus.Proposed),
                cancellationToken)
            .ConfigureAwait(false);
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
                p.OriginalProposedCategoryId,
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
                proposedCategory: (CommodityCategory)row.ProposedCategoryId,
                originalProposedCategory: (CommodityCategory)row.OriginalProposedCategoryId,
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
                p.OriginalProposedCategoryId,
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
            proposedCategory: (CommodityCategory)row.ProposedCategoryId,
            originalProposedCategory: (CommodityCategory)row.OriginalProposedCategoryId,
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

        // ADR-034 п. 2 (B1): единственное место вычисления и записи очищенного ключа
        // строки очереди; NormalizeName вызывается ровно один раз на строку.
        var normalizedName = CommodityNameNormalizer.NormalizeName(candidate.Name);

        if (normalizedName.Length > MaxNameLength)
        {
            // FR-4.7: lowercase-расширение не должно молча обрезаться в varchar(256)
            // (аналог preflight из миграции 20261109120000 для новых строк).
            _logger.LogWarning(
                "Skipping precategorization proposal with normalized name length {NormalizedNameLength} (raw name length {NameLength}, merchant {MerchantId}).",
                normalizedName.Length,
                candidate.Name.Length,
                candidate.MerchantId);
            return false;
        }

        var entity = new PrecategorizedCommodityEntity
        {
            Id = Guid.NewGuid(),
            Name = candidate.Name,
            NormalizedName = normalizedName,
            MerchantId = candidate.MerchantId,
            ProposedCategoryId = (int)category,
            // ADR-033 п. 1 (D1): единственное место записи исходного предложения — фоновая вставка.
            OriginalProposedCategoryId = (int)category,
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
            // FR-4.5: unique-конфликт — параллельная вставка, no-op. Catch покрывает оба
            // индекса: ux_precategorized_commodities_name_merchant (name, merchant_id) и
            // частичный ux_precategorized_commodities_normalized_proposed (normalized_name)
            // WHERE status = 0 (ADR-034 п. 2).
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

        // FR-6.1–6.3: товары ВСЕХ пользователей с точно таким же наименованием, только без
        // категории (NULL или Undefined). Шаг общий с массовым путём (ADR-036 §2.1):
        // предикат и сеттеры не дублируются, поведение поштучного confirm не меняется.
        var updatedCommodities = await UpdateCommoditiesByNameAsync(entity.Name, category, cancellationToken)
            .ConfigureAwait(false);

        entity.Status = PrecategorizedCommodityStatus.Applied;
        entity.AppliedAt = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // «Сестринских» proposed-строк с тем же очищенным ключом не существует: глобальный
        // partial unique ux_precategorized_commodities_normalized_proposed гарантирует, что
        // подтверждаемая строка — единственная proposed своего ключа (ADR-034 D1/D4), поэтому
        // confirm их не ищет и не удаляет. Прежняя ветка delete по сырому имени удалена
        // как мёртвая ветка (миграция 20261109120000 сняла все proposed).

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

    public async Task<IReadOnlyCollection<BulkConfirmRepositoryItem>> ConfirmManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Защитно: пустой список не открывает транзакцию (валидация — на API, ADR-036 §1.3).
        if (ids.Count == 0)
        {
            return Array.Empty<BulkConfirmRepositoryItem>();
        }

        var distinctIds = ids.Distinct().ToArray();

        // ADR-036 F1/§2.2: уровень изоляции указывается явно. В REPEATABLE READ/SERIALIZABLE
        // конкурентное обновление дало бы 40001 (технический сбой с полным откатом) вместо
        // требуемого пропуска already-applied.
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        // Снимок + блокировка + классификация одним запросом (ADR-036 §2.1/C3). Строки с
        // status = applied попали в снимок уже обработанными (-> Conflict), отсутствующие в
        // снимке id — not-found: логический конфликт не прерывает транзакцию (C1).
        var snapshot = await LoadLockedSnapshotAsync(distinctIds, cancellationToken).ConfigureAwait(false);
        var snapshotById = snapshot.ToDictionary(row => row.Id);

        // Классификация по снимку: proposed — применимые строки, applied — уже обработанные
        // (Conflict), отсутствующих в снимке id не видно вовсе (NotFound).
        var proposedRows = snapshot.Where(IsProposed).ToList();

        // Категория берётся из снимка (D1). Валидация всего списка — до первого UPDATE
        // (fail-fast): повреждённое значение (Undefined или вне реестра категорий) откатывает
        // операцию, не успев выполнить ни одного statement'а по commodities.
        var proposedUpdates = new List<(Guid Id, string Name, CommodityCategory Category)>(proposedRows.Count);
        foreach (var row in proposedRows)
        {
            var category = (CommodityCategory)row.ProposedCategoryId;
            EnsureAssignableCategory(category);
            proposedUpdates.Add((row.Id, row.Name, category));
        }

        // Применение — в том же детерминированном порядке, что и блокировки снимка
        // (normalized_name, merchant_id, id): порядок блокировок commodities наш, а не порядок
        // сканирования (ADR-036 §2.1). Цикл только по наименованиям, очередь меняется set-based.
        var updatedCommoditiesById = new Dictionary<Guid, int>(proposedUpdates.Count);
        foreach (var (id, name, category) in proposedUpdates)
        {
            updatedCommoditiesById[id] = await UpdateCommoditiesByNameAsync(name, category, cancellationToken)
                .ConfigureAwait(false);
        }

        if (proposedUpdates.Count > 0)
        {
            // List, а не массив: в .NET 10 вызов Contains у массива связывается с
            // MemoryExtensions.Contains(ReadOnlySpan<T>) — это выражение EF транслировать не может.
            var appliedIds = proposedUpdates.Select(update => update.Id).ToList();

            // Одно значение applied_at на весь батч: одно чтение TimeProvider (ADR-036 §2.1);
            // set-based UPDATE вместо tracked-сущностей (в трекере нечего «отравлять»).
            var appliedAt = _timeProvider.GetUtcNow();
            await _dbContext.PrecategorizedCommodities
                .Where(p => appliedIds.Contains(p.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(p => p.Status, PrecategorizedCommodityStatus.Applied)
                        .SetProperty(p => p.AppliedAt, appliedAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Ровно один commit, последний: любое исключение выше откатывает весь список (B2).
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Bulk confirm classified {RequestedCount} ids: {AppliedCount} proposed, {ConflictCount} already applied, {NotFoundCount} not found.",
            ids.Count,
            proposedRows.Count,
            snapshot.Count - proposedRows.Count,
            distinctIds.Length - snapshot.Count);

        return BuildConfirmResults(ids, snapshotById, updatedCommoditiesById);
    }

    public async Task<IReadOnlyCollection<BulkDeclineRepositoryItem>> DeclineManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Array.Empty<BulkDeclineRepositoryItem>();
        }

        var distinctIds = ids.Distinct().ToArray();

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = await LoadLockedSnapshotAsync(distinctIds, cancellationToken).ConfigureAwait(false);
        var snapshotById = snapshot.ToDictionary(row => row.Id);

        // C4/§2.1: удаляются только классифицированные proposed-строки; applied остаётся,
        // commodities не меняются. List, а не массив: Contains массива в .NET 10 связывается
        // с MemoryExtensions.Contains(ReadOnlySpan<T>) и не транслируется EF.
        var proposedIds = snapshot
            .Where(IsProposed)
            .Select(row => row.Id)
            .ToList();

        if (proposedIds.Count > 0)
        {
            await _dbContext.PrecategorizedCommodities
                .Where(p => proposedIds.Contains(p.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Bulk decline classified {RequestedCount} ids: {DeclinedCount} proposed, {ConflictCount} already applied, {NotFoundCount} not found.",
            ids.Count,
            proposedIds.Count,
            snapshot.Count - proposedIds.Count,
            distinctIds.Length - snapshot.Count);

        return BuildDeclineResults(ids, snapshotById);
    }

    public async Task<IReadOnlyDictionary<string, CommodityCategory>> GetCategoriesByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedNames);

        var result = new Dictionary<string, CommodityCategory>(StringComparer.Ordinal);
        if (normalizedNames.Count == 0)
        {
            return result;
        }

        // ADR-034 п. 3: ключ — очищенное имя (normalized_name), вычисленное в C# и переданное
        // параметром (SQL-нормализации нет); sargable по ix_precategorized_commodities_normalized_name.
        var normalizedList = normalizedNames.Distinct(StringComparer.Ordinal).ToList();

        // FR-9.2: поиск по очищенному ключу в любом статусе, самая свежая строка при дублях.
        var rows = await _dbContext.PrecategorizedCommodities
            .AsNoTracking()
            .Where(p => normalizedList.Contains(p.NormalizedName))
            .OrderByDescending(p => p.AppliedAt ?? p.CreatedAt)
            .Select(p => new { p.NormalizedName, p.ProposedCategoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (result.ContainsKey(row.NormalizedName))
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

            result[row.NormalizedName] = category;
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

    /// <summary>
    /// Проверка категории, которую предкатегоризация присваивает товарам или хранит в очереди:
    /// <c>Undefined</c> и значения вне реестра недопустимы в обоих сценариях (поштучный confirm,
    /// правка предложения, фоновая вставка, массовый confirm).
    /// </summary>
    private static void EnsureAssignableCategory(CommodityCategory category)
    {
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            throw new ArgumentException(
                "Category Undefined cannot be assigned to commodities or stored in the precategorization queue.",
                nameof(category));
        }
    }

    /// <summary>
    /// Общий с массовым путём шаг обновления <c>commodities</c> по <b>точному</b> имени
    /// (ADR-036 §2.1): предикат «только без категории» и сеттеры не дублируются. Поведение
    /// поштучного <see cref="ConfirmAsync"/> не меняется.
    /// </summary>
    private async Task<int> UpdateCommoditiesByNameAsync(
        string exactName,
        CommodityCategory category,
        CancellationToken cancellationToken)
    {
        var categoryName = CommodityCategoryHelper.GetDisplayName(category);

        // FR-6.1–6.3: товары ВСЕХ пользователей с точно таким же наименованием, только без
        // категории (NULL или Undefined). Доступ вне owner-scoping намеренный (ADR-027, D8/D10,
        // NFR-5): массовое применение категории — глобальная операция администратора.
        return await _dbContext.Commodities
            .IgnoreQueryFilters()
            .Where(c => c.Name == exactName && (c.CategoryId == null || c.CategoryId == 0))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.CategoryId, (int?)category)
                    .SetProperty(c => c.CategoryName, categoryName),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Снимок строк очереди под блокировкой (ADR-036 §2.1 шаг 1, C3): один raw-SQL без
    /// LINQ-композиции, порядок <c>(normalized_name, merchant_id, id)</c> — единственный
    /// допустимый, он же порядок взятия блокировок (§2.3). Колонки маппятся на свойства
    /// <see cref="BulkSnapshotRow"/> по snake_case-конвенции модели (прецедент —
    /// <see cref="CandidateRow"/>), поэтому явных алиасов нет.
    /// </summary>
    private async Task<IReadOnlyList<BulkSnapshotRow>> LoadLockedSnapshotAsync(
        Guid[] distinctIds,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, proposed_category_id, original_proposed_category_id, status
            FROM precategorized_commodities
            WHERE id = ANY(@p0)
            ORDER BY normalized_name, merchant_id, id
            FOR UPDATE
            """;

        var rows = await _dbContext.Database
            .SqlQueryRaw<BulkSnapshotRow>(sql, new object[] { distinctIds })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    /// <summary>
    /// Классификация строки снимка (ADR-036 §2.1 шаг 1): <c>true</c> — строка <c>proposed</c>
    /// и подлежит применению/удалению; <c>false</c> — уже <c>applied</c> (пропуск
    /// <c>already-applied</c>). Единственное место сравнения со статусом схемы, чтобы предикат
    /// не разъезжался между confirm и decline.
    /// </summary>
    private static bool IsProposed(BulkSnapshotRow row)
        => row.Status != (short)PrecategorizedCommodityStatus.Applied;

    /// <summary>
    /// Результат массового отклонения по одной записи на каждый переданный id, в порядке
    /// переданной коллекции (ADR-036 §1.2: порядок ответа задаёт запрос, а не снимок).
    /// </summary>
    private static IReadOnlyCollection<BulkDeclineRepositoryItem> BuildDeclineResults(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyDictionary<Guid, BulkSnapshotRow> snapshotById)
    {
        var results = new List<BulkDeclineRepositoryItem>(ids.Count);
        foreach (var id in ids)
        {
            if (!snapshotById.TryGetValue(id, out var row))
            {
                results.Add(new BulkDeclineRepositoryItem(id, PrecategorizedOperationOutcome.NotFound));
                continue;
            }

            results.Add(IsProposed(row)
                ? new BulkDeclineRepositoryItem(id, PrecategorizedOperationOutcome.Applied)
                : new BulkDeclineRepositoryItem(id, PrecategorizedOperationOutcome.Conflict));
        }

        return results;
    }

    /// <summary>
    /// Результат массового подтверждения по одной записи на каждый переданный id, в порядке
    /// переданной коллекции (ADR-036 §1.1: порядок ответа задаёт запрос, а не снимок).
    /// </summary>
    private static IReadOnlyCollection<BulkConfirmRepositoryItem> BuildConfirmResults(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyDictionary<Guid, BulkSnapshotRow> snapshotById,
        IReadOnlyDictionary<Guid, int> updatedCommoditiesById)
    {
        var results = new List<BulkConfirmRepositoryItem>(ids.Count);
        foreach (var id in ids)
        {
            if (!snapshotById.TryGetValue(id, out var row))
            {
                results.Add(new BulkConfirmRepositoryItem(
                    id, PrecategorizedOperationOutcome.NotFound, null, null, null, 0));
                continue;
            }

            if (!IsProposed(row))
            {
                results.Add(new BulkConfirmRepositoryItem(
                    id, PrecategorizedOperationOutcome.Conflict, row.Name, null, null, 0));
                continue;
            }

            results.Add(new BulkConfirmRepositoryItem(
                id,
                PrecategorizedOperationOutcome.Applied,
                row.Name,
                (CommodityCategory)row.OriginalProposedCategoryId,
                (CommodityCategory)row.ProposedCategoryId,
                updatedCommoditiesById[id]));
        }

        return results;
    }

    /// <summary>Строка raw-выборки кандидатов (пара name + merchant_id).</summary>
    private sealed class CandidateRow
    {
        public string Name { get; set; } = string.Empty;

        public Guid MerchantId { get; set; }
    }

    /// <summary>
    /// Строка снимка очереди под <c>SELECT … FOR UPDATE</c> (ADR-036 §2.1).
    /// <c>Status</c> — <c>smallint</c> схемы (<c>0</c> proposed, <c>1</c> applied).
    /// </summary>
    private sealed class BulkSnapshotRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int ProposedCategoryId { get; set; }

        public int OriginalProposedCategoryId { get; set; }

        public short Status { get; set; }
    }
}
