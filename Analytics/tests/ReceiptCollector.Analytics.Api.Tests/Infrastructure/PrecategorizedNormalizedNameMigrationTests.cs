using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Паритетный тест миграции <c>20261109120000_add_precategorized_normalized_name.sql</c>
/// (T-03, ADR-034 п. 2 «Идемпотентность и дрейф», ответ на открытый вопрос 6): одноразовая
/// SQL-копия правила очистки в backfill не должна расходиться с
/// <see cref="CommodityNameNormalizer"/>. Тест применяет РЕАЛЬНЫЙ файл скрипта из репозитория
/// (копируется в output тестовой сборки, см. csproj) целиком одним Npgsql-сеансом — как это
/// делает <c>MigrationRunner</c>. Транзакция на скрипт и учёт в
/// <c>migration_scripts_history</c> — свойства раннера (покрыты его прецедентами), здесь
/// не моделируются; повторный прогон скрипта не проверяется (план T-03, «Что НЕ входит»).
/// </summary>
/// <remarks>
/// Preflight (<c>RAISE EXCEPTION</c> при длине ключа &gt; 256) отдельным тестом
/// НЕ покрывается: воспроизводимое расширение строки при <c>lower()</c> в PostgreSQL иное,
/// чем <c>ToLowerInvariant()</c> в .NET (допущения ADR-034; в .NET 10 ICU
/// <c>ToLowerInvariant</c> строку не расширяет — факт зафиксирован тестом T-02
/// <c>TryAddProposalAsync_normalized_name_never_exceeds_raw_length_in_current_runtime</c>),
/// поэтому аномальное удлинение в тесте воспроизвести нельзя. Preflight остаётся страховкой
/// на реальных данных и проверяется контрольными запросами на dev (план, раздел
/// «Ответ на открытый вопрос №6 ADR»).
/// </remarks>
public sealed class PrecategorizedNormalizedNameMigrationTests : IAsyncLifetime
{
    private const string MigrationScriptFileName = "20261109120000_add_precategorized_normalized_name.sql";

    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public PrecategorizedNormalizedNameMigrationTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task MigrationScript_backfills_applied_per_normalizer_and_finalizes_schema()
    {
        var appliedMerchantId = Guid.NewGuid();
        var proposedMerchantId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.AddRange(
                CreateMerchant(appliedMerchantId, "Applied Store"),
                CreateMerchant(proposedMerchantId, "Proposed Store"));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            // Приводим таблицу к «до-миграционному» виду: EnsureCreatedAsync строит актуальную
            // EF-схему (уже с normalized_name), а миграцию нужно применить к схеме «до неё».
            // DROP COLUMN заодно удаляет индексы по колонке (ix_..._normalized_name и
            // ux_..._normalized_proposed) — скрипт создаёт их заново точными именами из
            // EF-конфига и SQL, поэтому конфликта при повторном создании нет.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE precategorized_commodities DROP COLUMN normalized_name");

            // applied-строки — весь golden corpus через raw SQL по «старым» колонкам
            // (normalized_name в схеме ещё нет): паритет backfill ↔ C# сверяется по всему
            // корпусу (план T-03 / ответ на открытый вопрос 6 ADR), а не по выборке —
            // иначе умышленная правка одного regex в скрипте может остаться незамеченной.
            var createdAt = DateTimeOffset.UtcNow;
            foreach (var (input, _) in CommodityNameNormalizationCorpus.Cases)
            {
                await context.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO precategorized_commodities
                        (id, name, merchant_id, proposed_category_id, original_proposed_category_id, status, created_at, applied_at)
                    VALUES
                        ({Guid.NewGuid()}, {input}, {appliedMerchantId}, 1, 1, 1, {createdAt}, {createdAt})
                    """);
            }

            // Две proposed-строки (имена из corpus, другой магазин) — миграция обязана
            // удалить их без backfill (FR-5: пересоберутся фоном по новому правилу).
            foreach (var name in new[] { "пирог 123456", "*3455614 РЖ.КРАЙ Хлеб нар.300г" })
            {
                await context.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO precategorized_commodities
                        (id, name, merchant_id, proposed_category_id, original_proposed_category_id, status, created_at)
                    VALUES
                        ({Guid.NewGuid()}, {name}, {proposedMerchantId}, 1, 1, 0, {createdAt})
                    """);
            }
        }

        // Выполняем файл скрипта из репозитория целиком одним Npgsql-сеансом (pg_temp
        // живёт только в сеансе миграции). Файл копируется в output сборкой (csproj
        // CopyToOutputDirectory), путь строится от каталога тестовой сборки и не хардкодится.
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", MigrationScriptFileName);
        var script = await File.ReadAllTextAsync(scriptPath);

        // Pooling=false — чтобы сеанс миграции и проверка «функция недоступна после сеанса»
        // гарантированно были разными бэкендами PostgreSQL.
        var dedicatedConnectionString =
            new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false }.ConnectionString;

        await using (var connection = new NpgsqlConnection(dedicatedConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(script, connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            // FR-5: все proposed удалены…
            Assert.Equal(
                0,
                await context.PrecategorizedCommodities.CountAsync(
                    p => p.Status == PrecategorizedCommodityStatus.Proposed,
                    CancellationToken.None));

            // …applied сохранены в прежнем количестве (те же строки, ничего не потеряно).
            var applied = await context.PrecategorizedCommodities
                .Where(p => p.Status == PrecategorizedCommodityStatus.Applied)
                .Select(p => new { p.Id, p.Name, p.NormalizedName })
                .ToListAsync(CancellationToken.None);

            Assert.Equal(CommodityNameNormalizationCorpus.Cases.Count, applied.Count);

            // Паритет SQL-backfill ↔ C# по всему corpus: для каждой applied-строки
            // normalized_name == CommodityNameNormalizer.NormalizeName(name) (и == ожиданию
            // corpus — тот же факт с другой стороны).
            foreach (var (input, expected) in CommodityNameNormalizationCorpus.Cases)
            {
                var row = Assert.Single(applied, a => a.Name == input);
                Assert.Equal(expected, row.NormalizedName);
                Assert.Equal(CommodityNameNormalizer.NormalizeName(input), row.NormalizedName);
            }
        }

        // NOT NULL после backfill: вставка без normalized_name отклоняется (23502 not_null_violation).
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO precategorized_commodities
                    (id, name, merchant_id, proposed_category_id, original_proposed_category_id, status, created_at)
                VALUES (gen_random_uuid(), 'Тест без ключа', @merchant, 1, 1, 0, now())
                """, connection);
            command.Parameters.AddWithValue("merchant", appliedMerchantId);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(CancellationToken.None));
            Assert.Equal("23502", exception.SqlState);
        }

        // Оба индекса по очищенному ключу созданы (имена зафиксированы в EF-конфиге и SQL).
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                "SELECT indexname FROM pg_indexes WHERE tablename = 'precategorized_commodities'",
                connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

            var indexNames = new List<string>();
            while (await reader.ReadAsync(CancellationToken.None))
            {
                indexNames.Add(reader.GetString(0));
            }

            Assert.Contains("ix_precategorized_commodities_normalized_name", indexNames);
            Assert.Contains("ux_precategorized_commodities_normalized_proposed", indexNames);
        }

        // pg_temp-функция существует только в сеансе миграции: в новом сеансе она недоступна
        // (шаг 9 — DROP FUNCTION, плюс временная схема сессии не видна другим сеансам).
        // Код ошибки зависит от сеанса: 42883 (функция не найдена) либо 3F000 (схема
        // pg_temp в новом сеансе ещё не создана) — оба означают «функции нет».
        await using (var connection = new NpgsqlConnection(dedicatedConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                "SELECT pg_temp.normalize_commodity_name('хлеб')", connection);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(CancellationToken.None));
            Assert.Contains(exception.SqlState, new[] { "42883", "3F000" });
        }
    }

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

    private static MerchantEntity CreateMerchant(Guid id, string name)
        => new() { Id = id, Name = name, Category = MerchantCategory.Undefined };
}
