using NSubstitute;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты админского сервиса предкатегоризации (шаг 6 плана, ADR-027 п.4, D10, FR-5.2–5.4, FR-6.4–6.6).
/// Подтверждение кладёт категорию в кэш только после Applied; Conflict/NotFound кэш не трогают.
/// </summary>
public class PrecategorizedCommodityServiceTests
{
    private readonly IPrecategorizedCommodityRepository _repository = Substitute.For<IPrecategorizedCommodityRepository>();

    [Fact]
    public async Task ConfirmAsync_applied_adds_normalized_name_to_cache()
    {
        var id = Guid.NewGuid();
        var proposal = CreateProposal(id, "  Молоко   3,2%  ");
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(proposal);
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(
                PrecategorizedOperationOutcome.Applied,
                proposal.Name,
                CommodityCategory.Dairy,
                3));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = new PrecategorizedCommodityService(_repository, cache);

        var result = await service.ConfirmAsync(id, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        Assert.Equal(id, result.Id);
        Assert.Equal((int)CommodityCategory.Dairy, result.CategoryId);
        Assert.Equal("Молочные продукты", result.CategoryName);
        Assert.Equal(3, result.UpdatedCommodities);

        // FR-6.4: ключ — нормализованное имя, поэтому поиск по другому регистру/пробелам работает.
        Assert.True(cache.TryGet("молоко 3,2%", out var cached));
        Assert.Equal(CommodityCategory.Dairy, cached);
    }

    [Fact]
    public async Task ConfirmAsync_conflict_does_not_touch_cache()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(CreateProposal(id, "Молоко"));
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(PrecategorizedOperationOutcome.Conflict, "Молоко", null, 0));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = new PrecategorizedCommodityService(_repository, cache);

        var result = await service.ConfirmAsync(id, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Conflict, result.Outcome);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ConfirmAsync_not_found_does_not_call_repository_confirm()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((PrecategorizedCommodity?)null);

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = new PrecategorizedCommodityService(_repository, cache);

        var result = await service.ConfirmAsync(id, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.NotFound, result.Outcome);
        Assert.Equal(0, cache.Count);
        await _repository.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
    }

    [Fact]
    public async Task ConfirmAsync_zero_updated_commodities_still_applies()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(CreateProposal(id, "Молоко"));
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(
                PrecategorizedOperationOutcome.Applied,
                "Молоко",
                CommodityCategory.Dairy,
                0));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = new PrecategorizedCommodityService(_repository, cache);

        var result = await service.ConfirmAsync(id, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        Assert.Equal(0, result.UpdatedCommodities);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task UpdateCategoryAsync_uses_repository_and_does_not_touch_commodities()
    {
        var id = Guid.NewGuid();
        _repository.UpdateProposedCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Applied);

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = new PrecategorizedCommodityService(_repository, cache);

        var outcome = await service.UpdateCategoryAsync(id, CommodityCategory.Dairy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, outcome);
        await _repository.Received(1).UpdateProposedCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>());
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task UpdateCategoryAsync_rejects_undefined_category()
    {
        var service = new PrecategorizedCommodityService(_repository, new InMemoryCommodityCategoryCache(10));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateCategoryAsync(Guid.NewGuid(), CommodityCategory.Undefined, CancellationToken.None));
        await _repository.DidNotReceiveWithAnyArgs().UpdateProposedCategoryAsync(default, default, default);
    }

    [Fact]
    public async Task DeclineAsync_maps_repository_outcomes()
    {
        var appliedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        _repository.DeleteProposedAsync(appliedId, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Conflict);
        _repository.DeleteProposedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.NotFound);

        var service = new PrecategorizedCommodityService(_repository, new InMemoryCommodityCategoryCache(10));

        Assert.Equal(PrecategorizedOperationOutcome.Conflict,
            await service.DeclineAsync(appliedId, CancellationToken.None));
        Assert.Equal(PrecategorizedOperationOutcome.NotFound,
            await service.DeclineAsync(missingId, CancellationToken.None));
    }

    [Fact]
    public async Task GetPageAsync_passes_paging_and_maps_dto()
    {
        var merchantId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        _repository.GetPageAsync(10, 20, "мол", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PrecategorizedCommodity(
                    Guid.NewGuid(),
                    "Молоко",
                    merchantId,
                    "Пятёрочка",
                    CommodityCategory.Dairy,
                    PrecategorizedCommodityStatus.Proposed,
                    createdAt)
            });
        _repository.GetPageCountAsync("мол", Arg.Any<CancellationToken>()).Returns(21);

        var service = new PrecategorizedCommodityService(_repository, new InMemoryCommodityCategoryCache(10));
        var (items, total) = await service.GetPageAsync(10, 20, "мол", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Молоко", item.Name);
        Assert.Equal(merchantId, item.MerchantId);
        Assert.Equal("Пятёрочка", item.MerchantName);
        Assert.Equal((int)CommodityCategory.Dairy, item.ProposedCategoryId);
        Assert.Equal("Молочные продукты", item.ProposedCategoryName);
        Assert.Equal(PrecategorizedCommodityStatusNames.Proposed, item.Status);
        Assert.Equal(createdAt, item.CreatedAt);
        Assert.Equal(21, total);
    }

    private static PrecategorizedCommodity CreateProposal(Guid id, string name)
        => new(
            id,
            name,
            Guid.NewGuid(),
            "Магазин",
            CommodityCategory.Dairy,
            PrecategorizedCommodityStatus.Proposed,
            DateTimeOffset.UtcNow);
}
