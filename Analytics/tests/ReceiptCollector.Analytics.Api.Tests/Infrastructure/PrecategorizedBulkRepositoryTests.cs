using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Сквозные тесты batch-репозитория очереди предкатегоризации (задача 02, ADR-036 §2):
/// одна транзакция <c>READ COMMITTED</c> на весь список, снимок под
/// <c>SELECT … FOR UPDATE</c> в порядке <c>(normalized_name, merchant_id, id)</c>,
/// классификация <c>proposed</c>/<c>applied</c>/<c>not-found</c> до изменений, set-based запись
/// очереди и ровно один commit. Реальные Postgres-контейнеры, как в
/// <see cref="PrecategorizedCommodityWorkflowTests"/>.
/// </summary>
public sealed class PrecategorizedBulkRepositoryTests : IAsyncLifetime
{
    private const string WaitingLockCountSql =
        """SELECT count(*)::int AS "Value" FROM pg_locks WHERE NOT granted""";

    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public PrecategorizedBulkRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task ConfirmMany_applies_proposed_rows_and_writes_single_applied_at()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [
                    CreateReceipt(userA, merchantA, ("Молоко", null)),
                    CreateReceipt(userA, merchantB, ("Хлеб", null), ("Хлеб", 0))
                ],
                [
                    Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchantB, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context)
                .ConfirmManyAsync([milkId, breadId], CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));

            var milk = Assert.Single(results, item => item.Id == milkId);
            Assert.Equal("Молоко", milk.Name);
            Assert.Equal(CommodityCategory.Dairy, milk.ProposedCategory);
            Assert.Equal(CommodityCategory.Dairy, milk.OriginalProposedCategory);
            Assert.Equal(1, milk.UpdatedCommodities);

            var bread = Assert.Single(results, item => item.Id == breadId);
            Assert.Equal(CommodityCategory.Bakery, bread.ProposedCategory);
            // Хлеб встречается дважды (NULL и 0) — обе строки товара обновлены.
            Assert.Equal(2, bread.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            var rows = await context.PrecategorizedCommodities.ToListAsync();
            Assert.All(rows, row => Assert.Equal(PrecategorizedCommodityStatus.Applied, row.Status));

            // Одно значение applied_at на весь батч (ADR-036 §2.1).
            var appliedAt = Assert.Single(rows.Select(row => row.AppliedAt).Distinct());
            Assert.NotNull(appliedAt);

            var commodities = await context.Commodities.ToListAsync();
            Assert.All(commodities, commodity => Assert.NotNull(commodity.CategoryId));
        }
    }

    [Fact]
    public async Task ConfirmMany_rolls_back_every_row_when_commodity_update_fails()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        // «Апельсин» сортируется раньше «Молоко»: к моменту сбоя первого товара второй уже
        // обновлён — тест доказывает, что откатывается и он (полный откат, решение 6).
        var orangeId = Guid.NewGuid();
        var milkId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, ("Апельсин", null), ("Молоко", null))],
                [
                    Proposal("Апельсин", merchant, CommodityCategory.Fruits, PrecategorizedCommodityStatus.Proposed, id: orangeId),
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId)
                ]);
        }

        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION test_bulk_confirm_fail() RETURNS trigger AS $$
                BEGIN
                    IF NEW.name = 'Молоко' THEN
                        RAISE EXCEPTION 'simulated commodity update failure';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER test_bulk_confirm_fail
                    BEFORE UPDATE ON commodities
                    FOR EACH ROW EXECUTE FUNCTION test_bulk_confirm_fail();
                """);
        }

        try
        {
            await using var context = CreateContext();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                CreateRepository(context).ConfirmManyAsync([orangeId, milkId], CancellationToken.None));
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS test_bulk_confirm_fail ON commodities");
            await cleanupContext.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS test_bulk_confirm_fail()");
        }

        await using (var context = CreateContext())
        {
            var rows = await context.PrecategorizedCommodities.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(PrecategorizedCommodityStatus.Proposed, row.Status));
            Assert.All(rows, row => Assert.Null(row.AppliedAt));

            // Ни одна строка commodities не осталась обновлённой — транзакция откатилась целиком.
            var commodities = await context.Commodities.ToListAsync();
            Assert.Equal(2, commodities.Count);
            Assert.All(commodities, commodity => Assert.Null(commodity.CategoryId));
            Assert.All(commodities, commodity => Assert.Null(commodity.CategoryName));
        }
    }

    [Fact]
    public async Task DeclineMany_rolls_back_every_row_when_delete_fails()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var orangeId = Guid.NewGuid();
        var milkId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                proposals:
                [
                    Proposal("Апельсин", merchant, CommodityCategory.Fruits, PrecategorizedCommodityStatus.Proposed, id: orangeId),
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId)
                ]);
        }

        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION test_bulk_decline_fail() RETURNS trigger AS $$
                BEGIN
                    IF OLD.name = 'Молоко' THEN
                        RAISE EXCEPTION 'simulated queue delete failure';
                    END IF;
                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER test_bulk_decline_fail
                    BEFORE DELETE ON precategorized_commodities
                    FOR EACH ROW EXECUTE FUNCTION test_bulk_decline_fail();
                """);
        }

        try
        {
            await using var context = CreateContext();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                CreateRepository(context).DeclineManyAsync([orangeId, milkId], CancellationToken.None));
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS test_bulk_decline_fail ON precategorized_commodities");
            await cleanupContext.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS test_bulk_decline_fail()");
        }

        await using (var context = CreateContext())
        {
            var rows = await context.PrecategorizedCommodities.ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(PrecategorizedCommodityStatus.Proposed, row.Status));
        }
    }

    [Fact]
    public async Task ConfirmMany_skips_already_applied_row_without_rewriting_it()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var appliedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var appliedId = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, ("Сыр", (int)CommodityCategory.Food), ("Молоко", null), ("Хлеб", null))],
                [
                    Proposal("Сыр", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied, appliedAt: appliedAt, id: appliedId),
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context)
                .ConfirmManyAsync([appliedId, milkId, breadId], CancellationToken.None);

            Assert.Equal(3, results.Count);
            var conflict = Assert.Single(results, item => item.Id == appliedId);
            Assert.Equal(PrecategorizedOperationOutcome.Conflict, conflict.Outcome);
            Assert.Equal(0, conflict.UpdatedCommodities);
            Assert.Null(conflict.ProposedCategory);
            Assert.All(
                results.Where(item => item.Id != appliedId),
                item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.FindAsync(appliedId);
            Assert.Equal(PrecategorizedCommodityStatus.Applied, stored!.Status);
            // Конфликт не переписывает уже подтверждённую строку: applied_at остался прежним
            // (а не равен времени батча). Tolerance — Postgres хранит timestamptz с точностью
            // до микросекунды, .NET — до 100 нс (прецедент: UserAuthLinkRepositoryTests).
            Assert.NotNull(stored.AppliedAt);
            Assert.Equal(appliedAt, stored.AppliedAt.Value, TimeSpan.FromMilliseconds(1));

            // Товар «Сыр» не перезаписан (пропущенная строка не даёт обновлений commodities).
            var cheese = Assert.Single(await context.Commodities.Where(c => c.Name == "Сыр").ToListAsync());
            Assert.Equal((int)CommodityCategory.Food, cheese.CategoryId);
            Assert.Equal("Прочая еда", cheese.CategoryName);

            Assert.Equal(PrecategorizedCommodityStatus.Applied,
                (await context.PrecategorizedCommodities.FindAsync(milkId))!.Status);
            Assert.Equal(PrecategorizedCommodityStatus.Applied,
                (await context.PrecategorizedCommodities.FindAsync(breadId))!.Status);
        }
    }

    [Fact]
    public async Task ConfirmMany_reports_not_found_and_applies_existing_rows()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, ("Молоко", null), ("Хлеб", null))],
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context)
                .ConfirmManyAsync([milkId, missingId, breadId], CancellationToken.None);

            Assert.Equal(3, results.Count);
            var missing = Assert.Single(results, item => item.Id == missingId);
            Assert.Equal(PrecategorizedOperationOutcome.NotFound, missing.Outcome);
            Assert.Null(missing.Name);
            Assert.Equal(0, missing.UpdatedCommodities);
            Assert.Equal(2, results.Count(item => item.Outcome == PrecategorizedOperationOutcome.Applied));
        }

        await using (var context = CreateContext())
        {
            Assert.All(
                await context.PrecategorizedCommodities.ToListAsync(),
                row => Assert.Equal(PrecategorizedCommodityStatus.Applied, row.Status));
        }
    }

    [Fact]
    public async Task DeclineMany_reports_not_found_and_deletes_only_proposed_rows()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                proposals:
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context)
                .DeclineManyAsync([milkId, missingId, breadId], CancellationToken.None);

            Assert.Equal(3, results.Count);
            Assert.Equal(PrecategorizedOperationOutcome.NotFound,
                Assert.Single(results, item => item.Id == missingId).Outcome);
            Assert.Equal(2, results.Count(item => item.Outcome == PrecategorizedOperationOutcome.Applied));
        }

        await using (var context = CreateContext())
        {
            Assert.Empty(await context.PrecategorizedCommodities.ToListAsync());
        }
    }

    [Fact]
    public async Task ConfirmMany_changes_exactly_the_requested_page_rows()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var proposals = Enumerable.Range(1, 15)
            .Select(index => Proposal(
                $"Товар {index:00}",
                merchant,
                CommodityCategory.Food,
                PrecategorizedCommodityStatus.Proposed))
            .ToList();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, proposals
                    .Select(proposal => (proposal.Name, (int?)null))
                    .ToArray())],
                proposals);
        }

        var pageIds = proposals.Take(10).Select(proposal => proposal.Id).ToList();
        var otherIds = proposals.Skip(10).Select(proposal => proposal.Id).ToHashSet();
        var otherNames = proposals.Skip(10).Select(proposal => proposal.Name).ToHashSet(StringComparer.Ordinal);

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context).ConfirmManyAsync(pageIds, CancellationToken.None);

            Assert.Equal(10, results.Count);
            Assert.All(results, item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));
        }

        await using (var context = CreateContext())
        {
            var rows = await context.PrecategorizedCommodities.ToListAsync();
            Assert.All(rows, row => Assert.Equal(
                otherIds.Contains(row.Id) ? PrecategorizedCommodityStatus.Proposed : PrecategorizedCommodityStatus.Applied,
                row.Status));

            var commodities = await context.Commodities.ToListAsync();
            Assert.All(
                commodities.Where(commodity => otherNames.Contains(commodity.Name)),
                commodity => Assert.Null(commodity.CategoryId));
            Assert.All(
                commodities.Where(commodity => !otherNames.Contains(commodity.Name)),
                commodity => Assert.Equal((int)CommodityCategory.Food, commodity.CategoryId));
        }
    }

    [Fact]
    public async Task ConfirmMany_is_safe_to_repeat_with_same_ids()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();
        var batchIds = new[] { milkId, breadId };

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, ("Молоко", null), ("Хлеб", null))],
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        DateTimeOffset[] appliedAtAfterFirstRun;

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.All(
                await repository.ConfirmManyAsync(batchIds, CancellationToken.None),
                item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));

            appliedAtAfterFirstRun = (await context.PrecategorizedCommodities
                .OrderBy(p => p.Id)
                .Select(p => p.AppliedAt!.Value)
                .ToListAsync()).ToArray();
            Assert.Equal(2, appliedAtAfterFirstRun.Length);

            // Повторный запуск тем же батчем: строки уже applied → пропуск без ошибки (ADR-036 §9).
            var repeated = await CreateRepository(context).ConfirmManyAsync(batchIds, CancellationToken.None);

            Assert.Equal(2, repeated.Count);
            Assert.All(repeated, item => Assert.Equal(PrecategorizedOperationOutcome.Conflict, item.Outcome));
            Assert.All(repeated, item => Assert.Equal(0, item.UpdatedCommodities));
        }

        await using (var context = CreateContext())
        {
            // Состояние данных не изменилось после повторного запуска: applied_at прежний,
            // товары не перезаписывались.
            var rows = await context.PrecategorizedCommodities.OrderBy(p => p.Id).ToListAsync();
            Assert.Equal(
                appliedAtAfterFirstRun,
                rows.Select(row => row.AppliedAt!.Value).ToArray());

            var commodities = await context.Commodities.ToListAsync();
            Assert.Equal((int)CommodityCategory.Dairy,
                Assert.Single(commodities, commodity => commodity.Name == "Молоко").CategoryId);
            Assert.Equal((int)CommodityCategory.Bakery,
                Assert.Single(commodities, commodity => commodity.Name == "Хлеб").CategoryId);
        }
    }

    [Fact]
    public async Task DeclineMany_is_safe_to_repeat_with_same_ids()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();
        var batchIds = new[] { milkId, breadId };

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                proposals:
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.All(
                await repository.DeclineManyAsync(batchIds, CancellationToken.None),
                item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));

            // Повторный запуск: строк нет → пропуск not-found, без ошибки.
            var repeated = await repository.DeclineManyAsync(batchIds, CancellationToken.None);

            Assert.Equal(2, repeated.Count);
            Assert.All(repeated, item => Assert.Equal(PrecategorizedOperationOutcome.NotFound, item.Outcome));
        }

        await using (var context = CreateContext())
        {
            Assert.Empty(await context.PrecategorizedCommodities.ToListAsync());
        }
    }

    [Fact]
    public async Task ConfirmMany_updates_only_commodities_without_category_of_all_users()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var merchantC = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();
        var userD = Guid.NewGuid();
        var milkId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [
                    CreateReceipt(userA, merchantA, ("Молоко", null)),
                    CreateReceipt(userB, merchantB, ("Молоко", 0)),
                    CreateReceipt(userC, merchantC, ("Молоко", (int)CommodityCategory.Food)),
                    CreateReceipt(userD, merchantC, ("Молоко 1%", null))
                ],
                [Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId)]);
        }

        await using (var context = CreateContext())
        {
            // Owner-scope выставлен, но массовое подтверждение видит товары всех пользователей
            // (D8/NFR-5 — как поштучный confirm).
            context.SetCurrentUser(userA);

            var result = Assert.Single(
                await CreateRepository(context).ConfirmManyAsync([milkId], CancellationToken.None));

            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(2, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            var milk = await context.Commodities.Where(c => c.Name == "Молоко").ToListAsync();
            Assert.Equal(3, milk.Count);
            Assert.Equal(2, milk.Count(c => c.CategoryId == (int)CommodityCategory.Dairy));
            Assert.All(
                milk.Where(c => c.CategoryId == (int)CommodityCategory.Dairy),
                c => Assert.Equal("Молочные продукты", c.CategoryName));
            // Товар с уже присвоенной категорией не перезаписан.
            Assert.Single(milk, c => c.CategoryId == (int)CommodityCategory.Food);

            // Другое наименование не тронуто.
            var similar = Assert.Single(await context.Commodities.Where(c => c.Name == "Молоко 1%").ToListAsync());
            Assert.Null(similar.CategoryId);
        }
    }

    [Fact]
    public async Task ConfirmMany_processes_distinct_names_independently_and_tolerates_reversed_id_order()
    {
        await ClearDatabaseAsync();

        // Три прогона с обратным порядком id: детерминированный порядок блокировок
        // (normalized_name, merchant_id, id) не даёт deadlock/таймаута (ADR-036 §2.3).
        for (var run = 1; run <= 3; run++)
        {
            var merchant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var orangeName = $"Апельсин {run}";
            var milkName = $"Молоко {run}";
            var orangeId = Guid.NewGuid();
            var milkId = Guid.NewGuid();

            await using (var context = CreateContext())
            {
                await SeedAsync(
                    context,
                    [CreateReceipt(user, merchant, (orangeName, null), (milkName, null))],
                    [
                        Proposal(orangeName, merchant, CommodityCategory.Fruits, PrecategorizedCommodityStatus.Proposed, id: orangeId),
                        Proposal(milkName, merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId)
                    ]);
            }

            await using (var context = CreateContext())
            {
                var results = await CreateRepository(context)
                    .ConfirmManyAsync([milkId, orangeId], CancellationToken.None);

                Assert.Equal(2, results.Count);
                Assert.All(results, item => Assert.Equal(PrecategorizedOperationOutcome.Applied, item.Outcome));
                Assert.All(results, item => Assert.Equal(1, item.UpdatedCommodities));
            }

            await using (var context = CreateContext())
            {
                var commodities = await context.Commodities.ToListAsync();
                Assert.Equal((int)CommodityCategory.Fruits,
                    Assert.Single(commodities, c => c.Name == orangeName).CategoryId);
                Assert.Equal((int)CommodityCategory.Dairy,
                    Assert.Single(commodities, c => c.Name == milkName).CategoryId);
            }
        }
    }

    [Fact]
    public async Task ConfirmMany_waits_for_concurrent_confirm_and_classifies_row_as_conflict()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var lockedId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                proposals:
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: lockedId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: secondId),
                    Proposal("Кефир", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: thirdId)
                ]);
        }

        var concurrentAppliedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await using var lockContext = CreateContext();
        await using var concurrentTransaction = await lockContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // Вторая транзакция подтверждает одну из строк и удерживает блокировку до commit.
        await lockContext.Database.ExecuteSqlRawAsync(
            "UPDATE precategorized_commodities SET status = 1, applied_at = {0} WHERE id = {1}",
            concurrentAppliedAt,
            lockedId);

        await using var batchContext = CreateContext();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var batchTask = CreateRepository(batchContext)
            .ConfirmManyAsync([lockedId, secondId, thirdId], cancellation.Token);

        // Ждём, пока batch реально встанет на блокировке строки, и только затем коммитим вторую.
        await WaitForLockWaitAsync();

        await concurrentTransaction.CommitAsync();

        var results = await batchTask;

        Assert.Equal(3, results.Count);
        var conflict = Assert.Single(results, item => item.Id == lockedId);
        Assert.Equal(PrecategorizedOperationOutcome.Conflict, conflict.Outcome);
        Assert.Equal(2, results.Count(item => item.Outcome == PrecategorizedOperationOutcome.Applied));

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.FindAsync(lockedId);
            Assert.Equal(PrecategorizedCommodityStatus.Applied, stored!.Status);
            // Строка классифицирована по актуальной версии после ожидания блокировки:
            // applied_at — значение второй транзакции, а не батча (tolerance — микросекунды
            // Postgres против тиков .NET).
            Assert.NotNull(stored.AppliedAt);
            Assert.Equal(concurrentAppliedAt, stored.AppliedAt.Value, TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task DeclineMany_does_not_touch_commodities_and_keeps_applied_rows()
    {
        await ClearDatabaseAsync();

        var merchant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();
        var appliedId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                [CreateReceipt(user, merchant, ("Молоко", null), ("Хлеб", null), ("Сыр", null))],
                [
                    Proposal("Молоко", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: milkId),
                    Proposal("Хлеб", merchant, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, id: breadId),
                    Proposal("Сыр", merchant, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied, id: appliedId)
                ]);
        }

        await using (var context = CreateContext())
        {
            var results = await CreateRepository(context)
                .DeclineManyAsync([milkId, breadId, appliedId], CancellationToken.None);

            Assert.Equal(3, results.Count);
            Assert.Equal(2, results.Count(item => item.Outcome == PrecategorizedOperationOutcome.Applied));
            Assert.Equal(PrecategorizedOperationOutcome.Conflict,
                Assert.Single(results, item => item.Id == appliedId).Outcome);
        }

        await using (var context = CreateContext())
        {
            var rows = await context.PrecategorizedCommodities.ToListAsync();
            var remaining = Assert.Single(rows);
            Assert.Equal(appliedId, remaining.Id);

            // Отклонение не меняет commodities (C4/ADR-027).
            Assert.All(
                await context.Commodities.ToListAsync(),
                commodity =>
                {
                    Assert.Null(commodity.CategoryId);
                    Assert.Null(commodity.CategoryName);
                });
        }
    }

    [Fact]
    public async Task Bulk_methods_return_empty_result_for_empty_ids()
    {
        await using var context = CreateContext();
        var repository = CreateRepository(context);

        Assert.Empty(await repository.ConfirmManyAsync(Array.Empty<Guid>(), CancellationToken.None));
        Assert.Empty(await repository.DeclineManyAsync(Array.Empty<Guid>(), CancellationToken.None));
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
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

    private static PrecategorizedCommodityRepository CreateRepository(ReceiptDbContext context)
        => new(context, TimeProvider.System, NullLogger<PrecategorizedCommodityRepository>.Instance);

    /// <summary>
    /// Ожидание факта блокировки: строка batch-запроса видна в <c>pg_locks</c> как невыданная.
    /// Так коммит «второй» транзакции делается детерминированно, без sleep-эвристик.
    /// </summary>
    private async Task WaitForLockWaitAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using (var context = CreateContext())
            {
                var waiting = await context.Database
                    .SqlQueryRaw<int>(WaitingLockCountSql)
                    .SingleAsync();

                if (waiting > 0)
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Bulk confirm did not start waiting for a row lock.");
    }

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.CategorizationDecisions.ExecuteDeleteAsync();
        await context.PrecategorizedCommodities.ExecuteDeleteAsync();
        await context.Commodities.ExecuteDeleteAsync();
        await context.Receipts.ExecuteDeleteAsync();
        await context.Merchants.ExecuteDeleteAsync();
    }

    /// <summary>
    /// Сид магазинов/чеков/строк очереди. Магазины создаются для всех упомянутых id и
    /// идемпотентно (повторный вызов не дублирует PK).
    /// </summary>
    private static async Task SeedAsync(
        ReceiptDbContext context,
        IEnumerable<ReceiptEntity>? receipts = null,
        IEnumerable<PrecategorizedCommodityEntity>? proposals = null)
    {
        var receiptList = receipts?.ToList() ?? [];
        var proposalList = proposals?.ToList() ?? [];

        var merchantIds = receiptList.Select(receipt => receipt.MerchantId)
            .Concat(proposalList.Select(proposal => proposal.MerchantId))
            .Distinct()
            .ToList();
        var existingMerchantIds = await context.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .Select(merchant => merchant.Id)
            .ToListAsync();

        foreach (var merchantId in merchantIds.Except(existingMerchantIds))
        {
            context.Merchants.Add(new MerchantEntity
            {
                Id = merchantId,
                Name = $"Store {merchantId:N}",
                Category = MerchantCategory.Undefined
            });
        }

        context.Receipts.AddRange(receiptList);
        context.PrecategorizedCommodities.AddRange(proposalList);
        await context.SaveChangesAsync();
    }

    private static ReceiptEntity CreateReceipt(
        Guid userId,
        Guid merchantId,
        params (string Name, int? CategoryId)[] items)
    {
        var receiptId = Guid.NewGuid();
        return new ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = $"external-{receiptId:N}",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow,
            Items = items.Select(item => new CommodityEntity
            {
                Id = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = item.Name,
                Quantity = 1m,
                UnitPrice = 10m,
                Nds = 0,
                NdsSum = 0m,
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryId is null or 0
                    ? null
                    : CommodityCategoryHelper.GetDisplayName((CommodityCategory)item.CategoryId.Value)
            }).ToList()
        };
    }

    private static PrecategorizedCommodityEntity Proposal(
        string name,
        Guid merchantId,
        CommodityCategory category,
        PrecategorizedCommodityStatus status,
        Guid? id = null,
        DateTimeOffset? appliedAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            // ADR-034 п. 2 (B1): у каждой строки очереди есть очищенный ключ.
            NormalizedName = CommodityNameNormalizer.NormalizeName(name),
            MerchantId = merchantId,
            ProposedCategoryId = (int)category,
            // ADR-033 п. 1 (D1): исходное предложение = предложенной категории.
            OriginalProposedCategoryId = (int)category,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            AppliedAt = appliedAt
        };
}
