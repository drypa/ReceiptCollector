using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты админского сервиса предкатегоризации (шаг 6 плана, ADR-027 п.4, D10, FR-5.2–5.4, FR-6.4–6.6).
/// Подтверждение кладёт категорию в кэш только после Applied; Conflict/NotFound кэш не трогают.
/// T-06 (ADR-033 п. 5): Applied — дополнительно одно событие журнала «предложено → принято»
/// (suggested = исходное предложение строки, final = применённая категория, admin = true);
/// NotFound/Conflict/Decline — событий нет; сбой журнала не меняет результат (NFR-3).
/// </summary>
public class PrecategorizedCommodityServiceTests
{
    private readonly IPrecategorizedCommodityRepository _repository = Substitute.For<IPrecategorizedCommodityRepository>();
    private readonly ICategorizationDecisionRecorder _recorder = Substitute.For<ICategorizationDecisionRecorder>();
    private readonly Guid _acceptedBy = Guid.NewGuid();

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
        var service = CreateService(cache);

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

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
    public async Task ConfirmAsync_applied_records_single_draft_with_original_as_suggested_and_current_as_final()
    {
        var id = Guid.NewGuid();
        // Исходное предложение — Food; админ мог исправить его до Dairy (PUT .../category).
        var proposal = CreateProposal(id, "  Молоко   3,2%  ", originalCategory: CommodityCategory.Food);
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(proposal);
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(
                PrecategorizedOperationOutcome.Applied,
                proposal.Name,
                CommodityCategory.Dairy,
                2));

        var service = CreateService(new InMemoryCommodityCategoryCache(maxSize: 100));

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        // ADR-033 п. 5 (D1): одно событие на строку очереди с полными полями FR-1.
        await _recorder.Received(1).RecordAsync(
            Arg.Is<IReadOnlyCollection<CategorizationDecisionDraft>>(drafts =>
                drafts.Count == 1
                // Точное имя строки очереди (обрезка и нормализация — ответственность recorder'а).
                && drafts.Single().CommodityName == proposal.Name
                && drafts.Single().SuggestionSource == CategorizationDecisionSources.Precategorized
                && drafts.Single().SuggestedCategory == CommodityCategory.Food
                && drafts.Single().FinalCategory == CommodityCategory.Dairy
                && drafts.Single().AcceptedByUserId == _acceptedBy
                && drafts.Single().AcceptedByAdmin),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_records_only_after_repository_transaction_applied()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(CreateProposal(id, "Молоко"));

        var applied = false;
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                applied = true;
                return Task.FromResult(new ConfirmPrecategorizedResult(
                    PrecategorizedOperationOutcome.Applied,
                    "Молоко",
                    CommodityCategory.Dairy,
                    1));
            });

        var recorderObservedApplied = false;
        var recorderObservedCacheCount = 0;
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        _recorder
            .RecordAsync(Arg.Any<IReadOnlyCollection<CategorizationDecisionDraft>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                recorderObservedApplied = applied;
                // К тому моменту кэш уже пополнен — журнал идёт последним (порядок ADR-019/027).
                recorderObservedCacheCount = cache.Count;
                return Task.CompletedTask;
            });

        var service = CreateService(cache);
        await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        // NFR-3/C1: журнал вызывается строго после commit транзакции подтверждения — и после кэша.
        Assert.True(recorderObservedApplied);
        Assert.Equal(1, recorderObservedCacheCount);
    }

    [Fact]
    public async Task ConfirmAsync_conflict_does_not_touch_cache_or_record()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(CreateProposal(id, "Молоко"));
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(PrecategorizedOperationOutcome.Conflict, "Молоко", null, 0));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Conflict, result.Outcome);
        Assert.Equal(0, cache.Count);
        // Подтверждение не состоялось — событий журнала нет (ADR-033 п. 5).
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ConfirmAsync_not_found_does_not_call_repository_confirm_or_record()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((PrecategorizedCommodity?)null);

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.NotFound, result.Outcome);
        Assert.Equal(0, cache.Count);
        await _repository.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ConfirmAsync_zero_updated_commodities_still_applies_and_records()
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
        var service = CreateService(cache);

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        Assert.Equal(0, result.UpdatedCommodities);
        Assert.Equal(1, cache.Count);
        // ADR-033 п. 5: updatedCommodities = 0 — событие всё равно пишется, решение состоялось.
        await _recorder.Received(1).RecordAsync(
            Arg.Is<IReadOnlyCollection<CategorizationDecisionDraft>>(drafts => drafts.Count == 1),
            Arg.Any<CancellationToken>());
    }

    public static IEnumerable<object[]> RecorderFailures()
    {
        yield return [new InvalidOperationException("journal is down")];
        yield return [new OperationCanceledException()];
    }

    [Theory]
    [MemberData(nameof(RecorderFailures))]
    public async Task ConfirmAsync_ignores_recorder_failure(Exception failure)
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(CreateProposal(id, "Молоко"));
        _repository.ConfirmAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedResult(
                PrecategorizedOperationOutcome.Applied,
                "Молоко",
                CommodityCategory.Dairy,
                2));

        _recorder
            .RecordAsync(Arg.Any<IReadOnlyCollection<CategorizationDecisionDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmAsync(id, _acceptedBy, CancellationToken.None);

        // NFR-3: сбой журнала (даже нарушение контракта recorder'а) не меняет результат подтверждения.
        Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        Assert.Equal((int)CommodityCategory.Dairy, result.CategoryId);
        Assert.Equal("Молочные продукты", result.CategoryName);
        Assert.Equal(2, result.UpdatedCommodities);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task UpdateCategoryAsync_uses_repository_and_does_not_touch_commodities()
    {
        var id = Guid.NewGuid();
        _repository.UpdateProposedCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Applied);

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var outcome = await service.UpdateCategoryAsync(id, CommodityCategory.Dairy, CancellationToken.None);

        Assert.Equal(PrecategorizedOperationOutcome.Applied, outcome);
        await _repository.Received(1).UpdateProposedCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>());
        Assert.Equal(0, cache.Count);
        // PUT предложенной категории — не точка фиксации: событие даёт только confirm (ADR-033 п. 5).
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task UpdateCategoryAsync_rejects_undefined_category()
    {
        var service = CreateService(new InMemoryCommodityCategoryCache(10));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateCategoryAsync(Guid.NewGuid(), CommodityCategory.Undefined, CancellationToken.None));
        await _repository.DidNotReceiveWithAnyArgs().UpdateProposedCategoryAsync(default, default, default);
    }

    [Fact]
    public async Task DeclineAsync_maps_repository_outcomes_and_does_not_record()
    {
        var appliedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        _repository.DeleteProposedAsync(appliedId, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Conflict);
        _repository.DeleteProposedAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.NotFound);

        var service = CreateService(new InMemoryCommodityCategoryCache(10));

        Assert.Equal(PrecategorizedOperationOutcome.Conflict,
            await service.DeclineAsync(appliedId, CancellationToken.None));
        Assert.Equal(PrecategorizedOperationOutcome.NotFound,
            await service.DeclineAsync(missingId, CancellationToken.None));
        // Отклонение строки — удаления proposal: предложение не подтверждено, событий нет (ADR-033 п. 5).
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
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
                    CommodityCategory.Dairy,
                    PrecategorizedCommodityStatus.Proposed,
                    createdAt)
            });
        _repository.GetPageCountAsync("мол", Arg.Any<CancellationToken>()).Returns(21);

        var service = CreateService(new InMemoryCommodityCategoryCache(10));
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

    private PrecategorizedCommodityService CreateService(ICommodityCategoryCache cache)
        => new(_repository, cache, _recorder, NullLogger<PrecategorizedCommodityService>.Instance);

    private static PrecategorizedCommodity CreateProposal(
        Guid id,
        string name,
        CommodityCategory originalCategory = CommodityCategory.Dairy)
        => new(
            id,
            name,
            Guid.NewGuid(),
            "Магазин",
            CommodityCategory.Dairy,
            originalCategory,
            PrecategorizedCommodityStatus.Proposed,
            DateTimeOffset.UtcNow);
}
