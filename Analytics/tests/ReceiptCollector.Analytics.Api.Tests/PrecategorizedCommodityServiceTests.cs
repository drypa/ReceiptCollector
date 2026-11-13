using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
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

    // ---- Массовые операции (ADR-036 §1.1/§1.2/§3, задача 03 декомпозиции
    // docs/plans/precategorized-bulk-operations.md): счётчики, причины пропусков,
    // «commit → кэш → журнал», порядок ответа, наблюдаемость без ПД.

    [Fact]
    public async Task ConfirmManyAsync_all_applied_counts_and_fills_cache_per_applied_row()
    {
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();
        var ids = new[] { milkId, breadId };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                AppliedConfirmItem(milkId, "  Молоко   3,2%  ", updatedCommodities: 3),
                AppliedConfirmItem(breadId, "Хлеб", CommodityCategory.Bakery, CommodityCategory.Food, updatedCommodities: 2),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(2, result.Requested);
        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Skipped);
        // updatedCommodities в агрегате — сумма по applied-строкам (ADR-036 §1.1).
        Assert.Equal(5, result.UpdatedCommodities);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, item =>
        {
            Assert.Equal(BulkItemOutcomes.Applied, item.Outcome);
            Assert.Null(item.Reason);
        });

        // Кэш: ключ — нормализованное имя, значение — применённая категория (ADR-019).
        Assert.True(cache.TryGet("молоко 3,2%", out var milk));
        Assert.Equal(CommodityCategory.Dairy, milk);
        Assert.True(cache.TryGet("хлеб", out var bread));
        Assert.Equal(CommodityCategory.Bakery, bread);
    }

    [Fact]
    public async Task ConfirmManyAsync_records_once_with_drafts_after_cache_and_with_none_token()
    {
        var milkId = Guid.NewGuid();
        var breadId = Guid.NewGuid();
        var ids = new[] { milkId, breadId };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                // Исходное предложение — Food; админ мог исправить его до Dairy (PUT .../category).
                AppliedConfirmItem(milkId, "Молоко", CommodityCategory.Dairy, CommodityCategory.Food),
                AppliedConfirmItem(breadId, "Хлеб", CommodityCategory.Bakery, CommodityCategory.Food),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var recorderObservedCacheCount = -1;
        _recorder
            .RecordAsync(Arg.Any<IReadOnlyCollection<CategorizationDecisionDraft>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // Журнал — последняя БД-операция после commit: кэш к этому моменту уже пополнен.
                recorderObservedCacheCount = cache.Count;
                return Task.CompletedTask;
            });

        var service = CreateService(cache);
        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(2, result.Applied);
        Assert.Equal(2, recorderObservedCacheCount);

        // E1/ADR-033: один вызов recorder'а, по draft'у на каждую applied-строку (без группировки
        // по имени), suggested = исходное предложение снимка, final = применённая категория.
        await _recorder.Received(1).RecordAsync(
            Arg.Is<IReadOnlyCollection<CategorizationDecisionDraft>>(drafts =>
                drafts.Count == 2
                && drafts.All(draft =>
                    draft.SuggestionSource == CategorizationDecisionSources.Precategorized
                    && draft.AcceptedByUserId == _acceptedBy
                    && draft.AcceptedByAdmin)
                && drafts.Single(draft => draft.CommodityName == "Молоко").SuggestedCategory == CommodityCategory.Food
                && drafts.Single(draft => draft.CommodityName == "Молоко").FinalCategory == CommodityCategory.Dairy
                && drafts.Single(draft => draft.CommodityName == "Хлеб").SuggestedCategory == CommodityCategory.Food
                && drafts.Single(draft => draft.CommodityName == "Хлеб").FinalCategory == CommodityCategory.Bakery),
            // CancellationToken.None: данные уже зафиксированы, уход администратора не уничтожает события.
            Arg.Is<CancellationToken>(token => token == CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmManyAsync_mixed_outcomes_maps_reasons_and_skips_cache_and_drafts_for_skipped()
    {
        var appliedId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var ids = new[] { appliedId, conflictId, missingId };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                AppliedConfirmItem(appliedId, "Молоко", updatedCommodities: 4),
                new BulkConfirmRepositoryItem(conflictId, PrecategorizedOperationOutcome.Conflict, "Сыр", null, null, 0),
                new BulkConfirmRepositoryItem(missingId, PrecategorizedOperationOutcome.NotFound, null, null, null, 0),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(3, result.Requested);
        Assert.Equal(1, result.Applied);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(4, result.UpdatedCommodities);
        // Инвариант ответа: applied + skipped = requested.
        Assert.Equal(result.Requested, result.Applied + result.Skipped);

        var applied = result.Results.First(item => item.Id == appliedId);
        Assert.Equal(BulkItemOutcomes.Applied, applied.Outcome);
        Assert.Equal((int)CommodityCategory.Dairy, applied.CategoryId);
        Assert.Equal("Молочные продукты", applied.CategoryName);
        Assert.Equal(4, applied.UpdatedCommodities);

        // skipped-строки: только причина, поля категории и счётчика не заполняются (ADR-036 §1.1).
        var conflict = result.Results.First(item => item.Id == conflictId);
        Assert.Equal(BulkItemOutcomes.Skipped, conflict.Outcome);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, conflict.Reason);
        Assert.Null(conflict.CategoryId);
        Assert.Null(conflict.CategoryName);
        Assert.Null(conflict.UpdatedCommodities);

        var missing = result.Results.First(item => item.Id == missingId);
        Assert.Equal(BulkItemOutcomes.Skipped, missing.Outcome);
        Assert.Equal(BulkSkipReasons.NotFound, missing.Reason);

        // Для skipped нет ни записей кэша, ни draft'ов журнала.
        Assert.Equal(1, cache.Count);
        await _recorder.Received(1).RecordAsync(
            Arg.Is<IReadOnlyCollection<CategorizationDecisionDraft>>(drafts =>
                drafts.Count == 1 && drafts.Single().CommodityName == "Молоко"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmManyAsync_missing_repository_entry_maps_to_not_found_and_keeps_invariant()
    {
        var appliedId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var ids = new[] { appliedId, conflictId, missingId };
        // Нарушение контракта репозитория (защитная ветка): вернул меньше записей, чем id.
        // Отсутствующая запись трактуется как skipped/not-found — инвариант не ломается.
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                AppliedConfirmItem(appliedId, "Молоко", updatedCommodities: 1),
                new BulkConfirmRepositoryItem(conflictId, PrecategorizedOperationOutcome.Conflict, "Сыр", null, null, 0),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(3, result.Requested);
        Assert.Equal(1, result.Applied);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(result.Requested, result.Applied + result.Skipped);

        var missing = result.Results.First(item => item.Id == missingId);
        Assert.Equal(BulkItemOutcomes.Skipped, missing.Outcome);
        Assert.Equal(BulkSkipReasons.NotFound, missing.Reason);

        // Одна применённая строка — один draft и одна запись кэша, несмотря на неполный ответ.
        Assert.Equal(1, cache.Count);
        await _recorder.Received(1).RecordAsync(
            Arg.Is<IReadOnlyCollection<CategorizationDecisionDraft>>(drafts => drafts.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmManyAsync_all_skipped_is_not_an_error_and_records_nothing()
    {
        var conflictId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var ids = new[] { conflictId, missingId };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new BulkConfirmRepositoryItem(conflictId, PrecategorizedOperationOutcome.Conflict, "Сыр", null, null, 0),
                new BulkConfirmRepositoryItem(missingId, PrecategorizedOperationOutcome.NotFound, null, null, null, 0),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(2, result.Requested);
        Assert.Equal(0, result.Applied);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(0, result.UpdatedCommodities);
        Assert.Equal(0, cache.Count);
        // Все строки пропущены — событий журнала нет (recorder не вызывается вовсе).
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ConfirmManyAsync_empty_ids_returns_zeros_without_touching_anything()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.ConfirmManyAsync(Array.Empty<Guid>(), _acceptedBy, CancellationToken.None);

        Assert.Equal(0, result.Requested);
        Assert.Equal(0, result.Applied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.UpdatedCommodities);
        Assert.Empty(result.Results);
        await _repository.DidNotReceiveWithAnyArgs().ConfirmManyAsync(default!, default);
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ConfirmManyAsync_repository_failure_propagates_and_skips_cache_and_recorder()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyCollection<BulkConfirmRepositoryItem>>(
                new InvalidOperationException("db is down")));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        // Технический сбой: транзакция откатана, ничего не применено (решение 6) — исключение
        // пробрасывается (эндпоинт отдаёт 500), кэш и журнал не вызываются.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None));

        Assert.Equal(0, cache.Count);
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task ConfirmManyAsync_recorder_failure_does_not_change_counters_or_cache()
    {
        var id = Guid.NewGuid();
        var ids = new[] { id };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[] { AppliedConfirmItem(id, "Молоко", updatedCommodities: 2) });

        _recorder
            .RecordAsync(Arg.Any<IReadOnlyCollection<CategorizationDecisionDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("journal is down")));

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        // NFR-3: нарушение контракта recorder'а (тот не бросает) не меняет результат и не пробрасывается.
        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(1, result.Applied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, result.UpdatedCommodities);
        Assert.Equal(BulkItemOutcomes.Applied, Assert.Single(result.Results).Outcome);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task ConfirmManyAsync_results_follow_request_order_not_repository_order()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var fourthId = Guid.NewGuid();
        // Порядок запроса: applied, skipped, applied, skipped — перемешанный вход репозитория
        // (он обязан вернуть результат в порядке запроса, но сервис сопоставляет по id сам).
        var ids = new[] { firstId, secondId, thirdId, fourthId };
        _repository.ConfirmManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new BulkConfirmRepositoryItem(thirdId, PrecategorizedOperationOutcome.NotFound, null, null, null, 0),
                AppliedConfirmItem(firstId, "Молоко", updatedCommodities: 1),
                new BulkConfirmRepositoryItem(fourthId, PrecategorizedOperationOutcome.Conflict, "Сыр", null, null, 0),
                AppliedConfirmItem(secondId, "Хлеб", CommodityCategory.Bakery, CommodityCategory.Food, updatedCommodities: 2),
            });

        var service = CreateService(new InMemoryCommodityCategoryCache(maxSize: 100));

        var result = await service.ConfirmManyAsync(ids, _acceptedBy, CancellationToken.None);

        Assert.Equal(ids, result.Results.Select(item => item.Id));
        Assert.Equal(BulkItemOutcomes.Applied, result.Results.ElementAt(0).Outcome);
        Assert.Equal(BulkItemOutcomes.Applied, result.Results.ElementAt(1).Outcome);
        Assert.Equal(BulkSkipReasons.NotFound, result.Results.ElementAt(2).Reason);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, result.Results.ElementAt(3).Reason);
    }

    [Fact]
    public async Task DeclineManyAsync_maps_outcomes_and_does_not_touch_cache_or_recorder()
    {
        var declinedId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var ids = new[] { declinedId, conflictId, missingId };
        _repository.DeclineManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new BulkDeclineRepositoryItem(declinedId, PrecategorizedOperationOutcome.Applied),
                new BulkDeclineRepositoryItem(conflictId, PrecategorizedOperationOutcome.Conflict),
                new BulkDeclineRepositoryItem(missingId, PrecategorizedOperationOutcome.NotFound),
            });

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.DeclineManyAsync(ids, CancellationToken.None);

        Assert.Equal(3, result.Requested);
        Assert.Equal(1, result.Declined);
        Assert.Equal(2, result.Skipped);
        // Инвариант ответа: declined + skipped = requested; порядок — порядок запроса.
        Assert.Equal(result.Requested, result.Declined + result.Skipped);
        Assert.Equal(ids, result.Results.Select(item => item.Id));
        Assert.Equal(BulkItemOutcomes.Declined, result.Results.ElementAt(0).Outcome);
        Assert.Null(result.Results.ElementAt(0).Reason);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, result.Results.ElementAt(1).Reason);
        Assert.Equal(BulkSkipReasons.NotFound, result.Results.ElementAt(2).Reason);

        // Отклонение — не точка фиксации решения: кэш и журнал не затрагиваются (ADR-036 §3).
        Assert.Equal(0, cache.Count);
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task DeclineManyAsync_empty_ids_returns_zeros_without_touching_anything()
    {
        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        var service = CreateService(cache);

        var result = await service.DeclineManyAsync(Array.Empty<Guid>(), CancellationToken.None);

        Assert.Equal(0, result.Requested);
        Assert.Equal(0, result.Declined);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Results);
        await _repository.DidNotReceiveWithAnyArgs().DeclineManyAsync(default!, default);
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task DeclineManyAsync_repository_failure_propagates()
    {
        var ids = new[] { Guid.NewGuid() };
        _repository.DeclineManyAsync(ids, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyCollection<BulkDeclineRepositoryItem>>(
                new InvalidOperationException("db is down")));

        var service = CreateService(new InMemoryCommodityCategoryCache(maxSize: 100));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeclineManyAsync(ids, CancellationToken.None));
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    private static BulkConfirmRepositoryItem AppliedConfirmItem(
        Guid id,
        string name,
        CommodityCategory appliedCategory = CommodityCategory.Dairy,
        CommodityCategory originalCategory = CommodityCategory.Dairy,
        int updatedCommodities = 1)
        => new(id, PrecategorizedOperationOutcome.Applied, name, originalCategory, appliedCategory, updatedCommodities);

    private PrecategorizedCommodityService CreateService(ICommodityCategoryCache cache)
        => new(_repository, cache, _recorder, TimeProvider.System, NullLogger<PrecategorizedCommodityService>.Instance);

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
