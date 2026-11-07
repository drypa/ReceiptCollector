using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Маппинг журнала категоризации (T-02, ADR-033 п. 1–2): domain → entity и соответствие
/// EF-модели SQL-миграции <c>20261106120000_add_categorization_decisions</c>.
/// Модель строится без подключения; CHECK-поведение проверяет контейнерный
/// <see cref="CategorizationDecisionRepositoryTests"/>.
/// </summary>
public sealed class CategorizationDecisionMappingTests
{
    [Fact]
    public void Create_MapsAllDomainFields()
    {
        var acceptedBy = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);
        var decision = new CategorizationDecision(
            Guid.NewGuid(),
            "Молоко 3,2%",
            "молоко 3,2%",
            "precategorized",
            CommodityCategory.Dairy,
            CommodityCategory.Bakery,
            acceptedBy,
            AcceptedByAdmin: true,
            createdAt);

        var entity = CategorizationDecisionEntity.Create(decision);

        Assert.Equal(decision.Id, entity.Id);
        Assert.Equal("Молоко 3,2%", entity.CommodityName);
        Assert.Equal("молоко 3,2%", entity.NormalizedName);
        Assert.Equal("precategorized", entity.SuggestionSource);
        Assert.Equal((int)CommodityCategory.Dairy, entity.SuggestedCategoryId);
        Assert.Equal((int)CommodityCategory.Bakery, entity.FinalCategoryId);
        Assert.Equal(acceptedBy, entity.AcceptedBy);
        Assert.True(entity.AcceptedByAdmin);
        Assert.Equal(createdAt, entity.CreatedAt);
    }

    [Fact]
    public void Model_MatchesMigrationColumnsAndLengths()
    {
        using var context = CreateContext();
        // IsDescending и CHECK хранятся только в design-time модели (runtime-модель read-optimized).
        var model = context.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(CategorizationDecisionEntity))!;

        Assert.Equal("categorization_decisions", entityType.GetTableName());

        var commodityName = entityType.FindProperty(nameof(CategorizationDecisionEntity.CommodityName))!;
        Assert.Equal("commodity_name", commodityName.GetColumnName());
        Assert.Equal(256, commodityName.GetMaxLength());
        Assert.False(commodityName.IsNullable);

        var normalizedName = entityType.FindProperty(nameof(CategorizationDecisionEntity.NormalizedName))!;
        Assert.Equal("normalized_name", normalizedName.GetColumnName());
        Assert.Equal(256, normalizedName.GetMaxLength());
        Assert.False(normalizedName.IsNullable);

        var suggestionSource = entityType.FindProperty(nameof(CategorizationDecisionEntity.SuggestionSource))!;
        Assert.Equal("suggestion_source", suggestionSource.GetColumnName());
        Assert.Equal(32, suggestionSource.GetMaxLength());
        Assert.False(suggestionSource.IsNullable);

        Assert.False(entityType.FindProperty(nameof(CategorizationDecisionEntity.SuggestedCategoryId))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(CategorizationDecisionEntity.FinalCategoryId))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(CategorizationDecisionEntity.AcceptedBy))!.IsNullable);
        Assert.False(entityType.FindProperty(nameof(CategorizationDecisionEntity.AcceptedByAdmin))!.IsNullable);

        var createdAt = entityType.FindProperty(nameof(CategorizationDecisionEntity.CreatedAt))!;
        Assert.Equal("created_at", createdAt.GetColumnName());
        Assert.Equal("timestamp with time zone", createdAt.GetColumnType());
        Assert.False(createdAt.IsNullable);

        var checkConstraints = entityType.GetCheckConstraints().Select(c => c.Name).ToList();
        Assert.Contains("ck_categorization_decisions_suggested_not_undefined", checkConstraints);
        Assert.Contains("ck_categorization_decisions_final_non_negative", checkConstraints);
    }

    [Fact]
    public void Model_DeclaresAllThreeIndexesWithOrderingAndMismatchFilter()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(CategorizationDecisionEntity))!;

        var combination = Assert.Single(
            entityType.GetIndexes(),
            i => i.GetDatabaseName() == "ix_categorization_decisions_combination");
        Assert.Equal(
        [
            false, false, false, false, true, true
        ], combination.IsDescending);

        // EF хранит «все колонки DESC» как пустой список, а «все ASC» — как null.
        var byCreatedAt = Assert.Single(
            entityType.GetIndexes(),
            i => i.GetDatabaseName() == "ix_categorization_decisions_created_at");
        Assert.NotNull(byCreatedAt.IsDescending);
        Assert.Empty(byCreatedAt.IsDescending);

        var mismatch = Assert.Single(
            entityType.GetIndexes(),
            i => i.GetDatabaseName() == "ix_categorization_decisions_mismatch");
        Assert.NotNull(mismatch.IsDescending);
        Assert.Empty(mismatch.IsDescending);
        Assert.Equal("suggested_category_id <> final_category_id", mismatch.GetFilter());
    }

    [Fact]
    public void Model_MapsPrecategorizedOriginalProposedCategoryAsRequiredColumn()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(PrecategorizedCommodityEntity))!;

        var original = entityType.FindProperty(nameof(PrecategorizedCommodityEntity.OriginalProposedCategoryId))!;

        Assert.Equal("original_proposed_category_id", original.GetColumnName());
        Assert.False(original.IsNullable);
    }

    private static ReceiptDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql("Host=localhost;Database=mapping_check;Username=u;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options);
}
