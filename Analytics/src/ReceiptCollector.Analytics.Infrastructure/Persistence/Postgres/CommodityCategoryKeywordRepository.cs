using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// Репозиторий слов категоризации (ADR-035 п. 1, решение A1; таблица <c>commodity_category_keywords</c>).
/// Слова глобальные и общие для всех пользователей — query-filter на <c>DbSet</c> не ставится
/// (T-01), owner-scoping здесь не нужен. Категория — код enum без FK (ADR-005).
/// </summary>
/// <remarks>
/// Безопасное чтение кода категории — по образцу ADR-032: <see cref="Enum.IsDefined(Type, object)"/>
/// + явный пропуск <see cref="CommodityCategory.Undefined"/>; строки с битым кодом пропускаются
/// при чтении снимка, а не приводятся молча. То же правило распространено на <c>list_type</c>
/// (замечание ревью T-02): <see cref="KeywordListTypeNames.TryParse"/> вернул <c>false</c>
/// (ручная правка схемы в обход CHECK) — строка пропускается; молчаливое приведение к
/// <see cref="KeywordListType.Boost"/> запрещено.
/// </remarks>
internal sealed class CommodityCategoryKeywordRepository : ICommodityCategoryKeywordRepository
{
    private readonly ReceiptDbContext _dbContext;
    private readonly ILogger<CommodityCategoryKeywordRepository> _logger;

    public CommodityCategoryKeywordRepository(
        ReceiptDbContext dbContext,
        ILogger<CommodityCategoryKeywordRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyCollection<CommodityCategoryKeyword>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // Решение E1: снимок строится одним полным SELECT таблицы (сотни строк); дальше читают
        // только память. Проекция — технические колонки, конвертация в домен — в C# (без SQL-кастов).
        var rows = await _dbContext.CommodityCategoryKeywords
            .AsNoTracking()
            .Select(e => new { e.Id, e.CategoryId, e.ListType, e.Word, e.NormalizedWord })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<CommodityCategoryKeyword>(rows.Count);
        var skipped = 0;

        foreach (var row in rows)
        {
            // ADR-032 «безопасное чтение»: неизвестный код или Undefined (CHECK запрещает 0,
            // но ручная правка схемы возможна) — строку пропускаем, снимок не должен содержать
            // категорию, для которой не существует имени/группы в реестре.
            if (!Enum.IsDefined(typeof(CommodityCategory), row.CategoryId))
            {
                skipped++;
                continue;
            }

            var category = (CommodityCategory)row.CategoryId;
            if (category == CommodityCategory.Undefined)
            {
                skipped++;
                continue;
            }

            // Битый list_type (CHECK не пропускает, но строка могла попасть ручной правкой схемы):
            // пропускаем явно — молчаливое приведение к Boost запрещено (решение по замечанию T-02).
            if (!KeywordListTypeNames.TryParse(row.ListType, out var listType))
            {
                skipped++;
                continue;
            }

            result.Add(new CommodityCategoryKeyword(row.Id, category, listType, row.Word, row.NormalizedWord));
        }

        if (skipped > 0)
        {
            // В логах — только количество (ADR-035 п. 9: тексты слов в лог не попадают).
            _logger.LogWarning(
                "Skipped {SkippedCount} commodity category keyword rows with invalid category code or list type.",
                skipped);
        }

        return result;
    }

    public async Task<bool> ExistsAsync(
        CommodityCategory category,
        KeywordListType listType,
        string normalizedWord,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedWord);
        var categoryId = EnsureStorableCategory(category);
        var wireListType = KeywordListTypeNames.ToWireName(listType);

        // Адресность по паре «категория + список» (FR-5): слово в другой категории или другом
        // списке на ответ не влияет. Обслуживается индексом ux_..._category_list_word (префикс).
        return await _dbContext.CommodityCategoryKeywords
            .AsNoTracking()
            .AnyAsync(
                e => e.CategoryId == categoryId
                     && e.ListType == wireListType
                     && e.NormalizedWord == normalizedWord,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        CommodityCategory category,
        KeywordListType listType,
        CancellationToken cancellationToken = default)
    {
        var categoryId = EnsureStorableCategory(category);
        var wireListType = KeywordListTypeNames.ToWireName(listType);

        // Проверка лимита MaxWordsPerList перед вставкой (FR-5, A5): count по той же паре.
        return await _dbContext.CommodityCategoryKeywords
            .AsNoTracking()
            .CountAsync(
                e => e.CategoryId == categoryId && e.ListType == wireListType,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> AddAsync(
        CommodityCategoryKeyword keyword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        var categoryId = EnsureStorableCategory(keyword.Category);
        var wireListType = KeywordListTypeNames.ToWireName(keyword.ListType);

        var entity = new CommodityCategoryKeywordEntity
        {
            // Id берётся из записи: сервис генерирует его до вставки, чтобы вернуть созданный
            // элемент в теле ответа POST (ADR-035 п. 7). Guid.Empty — репозиторий создаёт новый.
            Id = keyword.Id == Guid.Empty ? Guid.NewGuid() : keyword.Id,
            CategoryId = categoryId,
            ListType = wireListType,
            // Слово пишется как передано (FR-5): сервис передаёт word после trim, регистр сохранён;
            // normalized_word — ключ уникальности, посчитанный сервисом (NormalizeWord).
            Word = keyword.Word,
            NormalizedWord = keyword.NormalizedWord
        };

        _dbContext.CommodityCategoryKeywords.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Гонка двух вкладок (FR-5, edge case «конкурентное редактирование»): уникальный
            // индекс ux_commodity_category_keywords_category_list_word — это false, а не
            // исключение: сервис отвечает 409, а не 500 (ADR-035 п. 7).
            _dbContext.Entry(entity).State = EntityState.Detached;
            _logger.LogDebug(
                "Commodity category keyword already exists (category {CategoryId}, list {ListType}); skipping.",
                categoryId,
                wireListType);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Одна строка по PK без загрузки (прецедент ExecuteUpdateAsync в ADR-027): нет строки —
        // 0 затронутых строк, false (повторный DELETE из другой вкладки даёт 404, ADR-035 п. 7).
        var deleted = await _dbContext.CommodityCategoryKeywords
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <summary>
    /// Категория адресуемо в хранилище только как валидный код реестра, отличный от
    /// <see cref="CommodityCategory.Undefined"/> (CHECK <c>category_id &lt;&gt; 0</c>).
    /// </summary>
    private static int EnsureStorableCategory(CommodityCategory category)
    {
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            throw new ArgumentException(
                "Undefined or unknown category cannot be addressed in the keyword storage.",
                nameof(category));
        }

        return (int)category;
    }
}
