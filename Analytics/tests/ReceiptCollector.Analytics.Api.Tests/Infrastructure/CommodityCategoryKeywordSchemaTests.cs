using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты схемы слов категоризации (T-01, ADR-035 п. 1–2): EF-модель и реальный файл миграции
/// <c>20261112120000_add_commodity_category_keywords.sql</c> обязаны давать одну и ту же схему.
/// Postgres-тесты создают схему из EF-модели (<c>EnsureCreatedAsync</c>) — как остальные
/// Postgres-тесты проекта, поэтому все инварианты (уникальный индекс, три CHECK) объявлены
/// и в <c>CommodityCategoryKeywordConfiguration</c>, и в SQL-скрипте. Тест миграции применяет
/// РЕАЛЬНЫЙ файл скрипта (копируется в output тестовой сборки, см. csproj): таблица, три
/// констрейнта, уникальный индекс и полный сид блока «Разграничения схожих категорий» (FR-2)
/// проверяются на пустой БД.
/// </summary>
public sealed class CommodityCategoryKeywordSchemaTests : IAsyncLifetime
{
    private const string MigrationScriptFileName = "20261112120000_add_commodity_category_keywords.sql";

    private const string UniqueIndexName = "ux_commodity_category_keywords_category_list_word";

    private const string CategoryNotUndefinedConstraint = "ck_commodity_category_keywords_category_not_undefined";
    private const string ListTypeConstraint = "ck_commodity_category_keywords_list_type";
    private const string WordLengthConstraint = "ck_commodity_category_keywords_word_length";

    /// <summary>
    /// Ожидаемое число строк сида — полная раскладка таблицы ADR-035 п. 2 по правилам 1–5:
    /// сухофрукты 10, соусы/приправы 16, колбасы 9, конфеты 17, консервы 8, бытовая техника 15,
    /// мебель 14, бытовая химия 33, рукоделие 13, снеки 15, фитнес 17. Итого boost 94, exclude 73.
    /// Значение фиксируется тестом: пропущенная/задвоенная строка сида — дефект T-01.
    /// </summary>
    private const int SeedRowCount = 167;

    private const int SeedBoostRowCount = 94;
    private const int SeedExcludeRowCount = 73;

    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public CommodityCategoryKeywordSchemaTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    // ===== EF-схема (EnsureCreatedAsync из EF-модели) =====

    /// <summary>
    /// FR-5, edge case «конкурентное редактирование»: дубликат <c>normalized_word</c> в одной паре
    /// «категория + список» держит БД (уникальный индекс), а то же слово в другом списке той же
    /// категории или в другой категории — легально.
    /// </summary>
    [Fact]
    public async Task Schema_unique_index_rejects_duplicate_in_same_list_and_allows_other_list_or_category()
    {
        await using var context = CreateContext();

        context.CommodityCategoryKeywords.AddRange(
            Keyword(CommodityCategory.Groceries, "boost", "Соль", "соль"),
            Keyword(CommodityCategory.Groceries, "exclude", "соль", "соль"),
            Keyword(CommodityCategory.Fruits, "boost", "соль", "соль"));
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.CommodityCategoryKeywords.CountAsync());

        var exception = await InsertAndCaptureFailureAsync(
            Keyword(CommodityCategory.Groceries, "boost", "соль", "соль"));

        // PostgreSQL SQLSTATE 23505 = unique_violation; проверяем именно целевой индекс.
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(UniqueIndexName, postgresException.ConstraintName);
    }

    /// <summary>
    /// FR-1/FR-5: закрытое множество типов списка, запрет <c>Undefined</c> (0) и границы длины
    /// слова (2–64) держатся CHECK-констрейнтами EF-схемы с теми же именами, что в SQL-миграции.
    /// </summary>
    [Fact]
    public async Task Schema_check_constraints_match_sql_migration_and_enforce_closed_rules()
    {
        // 0 (Undefined) запрещён: справочника категорий нет, «нет категории» словом не бывает.
        var categoryException = await InsertAndCaptureFailureAsync(
            Keyword(CommodityCategory.Undefined, "boost", "соль", "соль"));
        var categoryPostgresException = Assert.IsType<PostgresException>(categoryException.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, categoryPostgresException.SqlState);
        Assert.Equal(CategoryNotUndefinedConstraint, categoryPostgresException.ConstraintName);

        // Множество типов списка закрыто требованием FR-1 («два списка»).
        var listTypeException = await InsertAndCaptureFailureAsync(
            Keyword(CommodityCategory.Groceries, "other", "соль", "соль"));
        var listTypePostgresException = Assert.IsType<PostgresException>(listTypeException.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, listTypePostgresException.SqlState);
        Assert.Equal(ListTypeConstraint, listTypePostgresException.ConstraintName);

        // Слово из одного символа отклоняется CHECK'ом длины (varchar(64) его пропускает).
        var shortWordException = await InsertAndCaptureFailureAsync(
            Keyword(CommodityCategory.Groceries, "boost", "я", "я"));
        var shortWordPostgresException = Assert.IsType<PostgresException>(shortWordException.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, shortWordPostgresException.SqlState);
        Assert.Equal(WordLengthConstraint, shortWordPostgresException.ConstraintName);

        // Слово из 65 символов отклоняется раньше CHECK — ограничением типа varchar(64)
        // (22001 string_data_right_truncation). Инвариант «не длиннее 64» держится схемой.
        var longWordException = await InsertAndCaptureFailureAsync(
            Keyword(CommodityCategory.Groceries, "boost", new string('я', 65), new string('я', 65)));
        var longWordPostgresException = Assert.IsType<PostgresException>(longWordException.InnerException);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, longWordPostgresException.SqlState);

        // Границы 2 и 64 символа — допустимы.
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.AddRange(
            Keyword(CommodityCategory.Groceries, "boost", "мы", "мы"),
            Keyword(CommodityCategory.Fruits, "exclude", new string('я', 64), new string('я', 64)));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.CommodityCategoryKeywords.CountAsync());
    }

    // ===== Реальный файл миграции (SQL-скрипт) =====

    /// <summary>
    /// Скрипт миграции создаёт таблицу с тремя CHECK и уникальным индексом и вставляет полный сид
    /// блока «Разграничения схожих категорий» (FR-2, ADR-035 п. 2). Схема, построенная EF-моделью
    /// в <see cref="InitializeAsync"/>, удаляется, чтобы таблица создавалась именно скриптом.
    /// </summary>
    [Fact]
    public async Task MigrationScript_creates_table_constraints_index_and_full_seed()
    {
        var script = await ReadMigrationScriptAsync();

        await using (var context = CreateContext())
        {
            // EnsureCreatedAsync строит актуальную EF-схему (уже с таблицей) — приводим БД
            // к «до-миграционному» виду, чтобы проверить создание объекта самим скриптом.
            await context.Database.ExecuteSqlRawAsync("DROP TABLE commodity_category_keywords");
        }

        await ExecuteScriptAsync(script);

        // Таблица и уникальный индекс существуют.
        await using (var connection = await OpenConnectionAsync())
        {
            await using var tableCommand = new NpgsqlCommand(
                """
                SELECT count(*)
                  FROM information_schema.tables
                 WHERE table_schema = 'public' AND table_name = 'commodity_category_keywords'
                """, connection);
            Assert.Equal(1L, (long)(await tableCommand.ExecuteScalarAsync(CancellationToken.None))!);

            await using var indexCommand = new NpgsqlCommand(
                """
                SELECT indexdef
                  FROM pg_indexes
                 WHERE tablename = 'commodity_category_keywords' AND indexname = @name
                """, connection);
            indexCommand.Parameters.AddWithValue("name", UniqueIndexName);
            var indexDefinition = (string?)await indexCommand.ExecuteScalarAsync(CancellationToken.None);
            Assert.NotNull(indexDefinition);
            Assert.StartsWith("CREATE UNIQUE INDEX", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("(category_id, list_type, normalized_word)", indexDefinition, StringComparison.Ordinal);
        }

        // Три CHECK с точными именами из ADR-035 п. 1 (те же имена объявлены в EF-конфигурации).
        await using (var connection = await OpenConnectionAsync())
        {
            await using var constraintCommand = new NpgsqlCommand(
                """
                SELECT conname
                  FROM pg_constraint
                 WHERE conrelid = 'commodity_category_keywords'::regclass AND contype = 'c'
                 ORDER BY conname
                """, connection);

            var constraintNames = new List<string>();
            await using (var reader = await constraintCommand.ExecuteReaderAsync(CancellationToken.None))
            {
                while (await reader.ReadAsync(CancellationToken.None))
                {
                    constraintNames.Add(reader.GetString(0));
                }
            }

            Assert.Equal(
                new[] { CategoryNotUndefinedConstraint, ListTypeConstraint, WordLengthConstraint }
                    .OrderBy(name => name, StringComparer.Ordinal),
                constraintNames.OrderBy(name => name, StringComparer.Ordinal));
        }

        // Сид: строк ровно столько, сколько в таблице ADR-035 п. 2, ON CONFLICT ничего не потерял.
        var seedBlock = ExtractSeedBlock(script);
        Assert.Equal(SeedRowCount, Regex.Matches(seedBlock, "gen_random_uuid\\(\\)").Count);

        await using (var context = CreateContext())
        {
            Assert.Equal(SeedRowCount, await context.CommodityCategoryKeywords.CountAsync());
            Assert.Equal(
                SeedBoostRowCount,
                await context.CommodityCategoryKeywords.CountAsync(k => k.ListType == "boost"));
            Assert.Equal(
                SeedExcludeRowCount,
                await context.CommodityCategoryKeywords.CountAsync(k => k.ListType == "exclude"));

            // Точечные проверки ключевых строк блока: субъект → boost, слова субъекта →
            // exclude категории-цели, перечисления в скобках → boost категории-цели.
            Assert.True(await ExistsAsync(context, CommodityCategory.DriedFruits, "boost", "изюм"));
            Assert.True(await ExistsAsync(context, CommodityCategory.Fruits, "exclude", "изюм"));
            Assert.True(await ExistsAsync(context, CommodityCategory.Groceries, "boost", "соль"));
            Assert.True(await ExistsAsync(context, CommodityCategory.FitnessAndSports, "boost", "абонемент"));

            // Словоформы приведены к форме, входящей подстрокой в исходную форму блока.
            Assert.True(await ExistsAsync(context, CommodityCategory.DriedFruits, "boost", "финик"));
            Assert.False(await ExistsAsync(context, CommodityCategory.DriedFruits, "boost", "финики"));
        }
    }

    /// <summary>
    /// ADR-035 B1: <c>ON CONFLICT DO NOTHING</c> — страховка ручного/частичного повторного
    /// выполнения того же скрипта. Повтор блока сида не падает и не меняет количество строк
    /// (удалённое админом слово не воскресает: штатная идемпотентность — migration_scripts_history).
    /// </summary>
    [Fact]
    public async Task MigrationScript_seed_can_be_replayed_without_changes()
    {
        var script = await ReadMigrationScriptAsync();

        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE commodity_category_keywords");
        }

        await ExecuteScriptAsync(script);
        var seedBlock = ExtractSeedBlock(script);

        await using (var context = CreateContext())
        {
            Assert.Equal(SeedRowCount, await context.CommodityCategoryKeywords.CountAsync());
        }

        await ExecuteScriptAsync(seedBlock);
        await ExecuteScriptAsync(seedBlock);

        await using (var context = CreateContext())
        {
            Assert.Equal(SeedRowCount, await context.CommodityCategoryKeywords.CountAsync());
        }
    }

    // ===== Инфраструктура =====

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync(CancellationToken.None);
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention();

        return new ReceiptDbContext(builder.Options);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken.None);
        return connection;
    }

    /// <summary>
    /// Вставляет строку в отдельном контексте и возвращает исключение. Отдельный контекст нужен,
    /// потому что после неудачного <c>SaveChangesAsync</c> сущность остаётся отслеживаемой и
    /// повторное сохранение того же контекста снова упало бы на ней же.
    /// </summary>
    private async Task<DbUpdateException> InsertAndCaptureFailureAsync(CommodityCategoryKeywordEntity entity)
    {
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.Add(entity);

        return await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static Task<bool> ExistsAsync(
        ReceiptDbContext context,
        CommodityCategory category,
        string listType,
        string normalizedWord)
        => context.CommodityCategoryKeywords.AnyAsync(k =>
            k.CategoryId == (int)category &&
            k.ListType == listType &&
            k.NormalizedWord == normalizedWord);

    private static CommodityCategoryKeywordEntity Keyword(
        CommodityCategory category,
        string listType,
        string word,
        string normalizedWord)
        => new()
        {
            Id = Guid.NewGuid(),
            CategoryId = (int)category,
            ListType = listType,
            Word = word,
            NormalizedWord = normalizedWord
        };

    private static async Task<string> ReadMigrationScriptAsync()
    {
        // Файл копируется в output сборкой (csproj CopyToOutputDirectory), путь строится
        // от каталога тестовой сборки и не хардкодится.
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", MigrationScriptFileName);
        return await File.ReadAllTextAsync(scriptPath);
    }

    private async Task ExecuteScriptAsync(string script)
    {
        // Скрипт выполняется целиком одним Npgsql-сеансом — как это делает MigrationRunner
        // (транзакция на скрипт и учёт в migration_scripts_history — свойства раннера).
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Вырезает блок сида между маркерами <c>-- seed:begin</c> / <c>-- seed:end</c> (ADR-035 п. 1:
    /// маркеры нужны только для повторного выполнения сида и не влияют на сам скрипт).
    /// </summary>
    private static string ExtractSeedBlock(string script)
    {
        const string beginMarker = "-- seed:begin";
        const string endMarker = "-- seed:end";

        var begin = script.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = script.IndexOf(endMarker, StringComparison.Ordinal);

        Assert.True(begin >= 0 && end > begin, "В скрипте миграции должны быть маркеры -- seed:begin/-- seed:end.");

        return script[(begin + beginMarker.Length)..end];
    }
}
