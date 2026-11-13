using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Api.Tests;

// Контракт данных массовых операций (ADR-036 §1.1/§1.2): wire-имена исходов и причин
// фиксируются дословно, набор допустимых значений — ровно тот, что ждёт фронтенд
// и endpoint-тесты. Тесты ловят случайное переименование констант.
public class PrecategorizedBulkModelsTests
{
    [Fact]
    public void Wire_constants_match_adr_wire_names_verbatim()
    {
        Assert.Equal("applied", BulkItemOutcomes.Applied);
        Assert.Equal("declined", BulkItemOutcomes.Declined);
        Assert.Equal("skipped", BulkItemOutcomes.Skipped);
        Assert.Equal("already-applied", BulkSkipReasons.AlreadyApplied);
        Assert.Equal("not-found", BulkSkipReasons.NotFound);
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("declined")]
    [InlineData("skipped")]
    public void BulkItemOutcomes_All_contains_contract_wire_name(string wireName)
    {
        Assert.Contains(wireName, BulkItemOutcomes.All);
    }

    [Theory]
    [InlineData("already-applied")]
    [InlineData("not-found")]
    public void BulkSkipReasons_All_contains_contract_wire_name(string wireName)
    {
        Assert.Contains(wireName, BulkSkipReasons.All);
    }

    [Fact]
    public void BulkItemOutcomes_All_contains_exactly_the_contract_values()
    {
        Assert.Equal(
            new[] { "applied", "declined", "skipped" },
            BulkItemOutcomes.All.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, BulkItemOutcomes.All.Count);
    }

    [Fact]
    public void BulkSkipReasons_All_contains_exactly_the_contract_values()
    {
        Assert.Equal(
            new[] { "already-applied", "not-found" },
            BulkSkipReasons.All.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, BulkSkipReasons.All.Count);
    }

    [Theory]
    [InlineData("Applied")]
    [InlineData("APPLIED")]
    [InlineData(" applied")]
    [InlineData("applied ")]
    [InlineData("already_applied")]
    [InlineData("")]
    public void All_sets_compare_ordinal_and_reject_non_contract_values(string value)
    {
        Assert.DoesNotContain(value, BulkItemOutcomes.All);
        Assert.DoesNotContain(value, BulkSkipReasons.All);
    }

    [Fact]
    public void BulkConfirmResult_allows_skipped_item_without_category_and_counter()
    {
        var skipped = new BulkConfirmItemResult(
            Guid.NewGuid(),
            BulkItemOutcomes.Skipped,
            BulkSkipReasons.AlreadyApplied,
            CategoryId: null,
            CategoryName: null,
            UpdatedCommodities: null);

        var applied = new BulkConfirmItemResult(
            Guid.NewGuid(),
            BulkItemOutcomes.Applied,
            Reason: null,
            CategoryId: 23,
            CategoryName: "Молочные продукты",
            UpdatedCommodities: 0);

        var result = new BulkConfirmResult(
            Requested: 2,
            Applied: 1,
            Skipped: 1,
            UpdatedCommodities: 0,
            Results: new[] { applied, skipped });

        Assert.Null(skipped.CategoryId);
        Assert.Null(skipped.CategoryName);
        Assert.Null(skipped.UpdatedCommodities);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, skipped.Reason);
        // 0 обновлённых товаров — решение всё равно состоялось (D10 ADR-027).
        Assert.Equal(0, applied.UpdatedCommodities);
        Assert.Null(applied.Reason);
        Assert.Equal(result.Requested, result.Applied + result.Skipped);
        Assert.Equal(new[] { applied, skipped }, result.Results.ToArray());
    }

    [Fact]
    public void BulkDeclineResult_allows_skipped_item_and_keeps_invariant()
    {
        var declined = new BulkDeclineItemResult(
            Guid.NewGuid(),
            BulkItemOutcomes.Declined,
            Reason: null);
        var skipped = new BulkDeclineItemResult(
            Guid.NewGuid(),
            BulkItemOutcomes.Skipped,
            BulkSkipReasons.NotFound);

        var result = new BulkDeclineResult(
            Requested: 2,
            Declined: 1,
            Skipped: 1,
            Results: new[] { declined, skipped });

        Assert.Null(declined.Reason);
        Assert.Equal(BulkSkipReasons.NotFound, skipped.Reason);
        Assert.Equal(result.Requested, result.Declined + result.Skipped);
        Assert.Equal(new[] { declined, skipped }, result.Results.ToArray());
        // В контракте отклонения нет updatedCommodities (асимметрия §1.2 намеренная).
        Assert.Null(typeof(BulkDeclineResult).GetProperty("UpdatedCommodities"));
    }
}
