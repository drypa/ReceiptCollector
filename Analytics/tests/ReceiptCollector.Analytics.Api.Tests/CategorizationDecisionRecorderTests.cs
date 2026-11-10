using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты единой точки записи журнала категоризации (T-03, ADR-033 п. 6, C1/NFR-3):
/// фильтрация невалидных черновиков, единственная нормализация имени, серверное время,
/// один батч-INSERT и контракт «никогда не бросает».
/// Регресс T-08 (ADR-034 п. 4.4 / FR-4): новые события хранят сырое <c>commodity_name</c>
/// и очищенный <c>normalized_name</c>, накопленная история журнала не пересчитывается.
/// </summary>
public class CategorizationDecisionRecorderTests
{
    private const int MaxNameLength = 256;

    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 30, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly ICategorizationDecisionRepository _repository = Substitute.For<ICategorizationDecisionRepository>();

    private ICategorizationDecisionRecorder CreateRecorder()
        => new CategorizationDecisionRecorder(
            _repository,
            _timeProvider,
            NullLogger<CategorizationDecisionRecorder>.Instance);

    [Fact]
    public async Task RecordAsync_valid_draft_writes_event_with_normalized_name_and_server_fields()
    {
        var draft = CreateDraft("  Молоко   3,2%  ");

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), draft);

        var decision = Assert.Single(decisions);
        Assert.NotEqual(Guid.Empty, decision.Id);
        // CommodityName — исходное название без внешних пробелов; внутренние пробелы
        // схлопывает только NormalizeName (CommodityNameNormalizer), а не запись события.
        Assert.Equal("Молоко   3,2%", decision.CommodityName);
        Assert.Equal("молоко 3,2%", decision.NormalizedName);
        Assert.Equal(CategorizationDecisionSources.Ai, decision.SuggestionSource);
        Assert.Equal(CommodityCategory.Dairy, decision.SuggestedCategory);
        Assert.Equal(CommodityCategory.Bakery, decision.FinalCategory);
        Assert.Equal(_userId, decision.AcceptedBy);
        Assert.True(decision.AcceptedByAdmin);
        Assert.Equal(Now, decision.CreatedAt);
    }

    [Fact]
    public async Task RecordAsync_name_with_leading_ean_keeps_raw_name_and_cleans_normalized_name()
    {
        // ADR-034 п. 4.4 / F1, FR-4: сырое имя не переписывается, очистка живёт только
        // в производном normalized_name.
        var draft = CreateDraft("2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ");

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), draft);

        var decision = Assert.Single(decisions);
        Assert.Equal("2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ", decision.CommodityName);
        Assert.Equal("пирог с вишней по ре", decision.NormalizedName);
    }

    [Fact]
    public async Task RecordAsync_code_only_name_gets_non_empty_fallback_within_column_guard()
    {
        // FR-1/FR-4: имя из одного кода не даёт пустой ключ — нормализатор возвращает base.
        // Второй вариант проверяет форму fallback'а: вход `*  #3455614` после collapse даёт
        // `* #3455614`, то есть normalized_name не повторяет вход буквально.
        var codeOnly = CreateDraft("*3455614");
        var noisyCodeOnly = CreateDraft("*  #3455614");

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), codeOnly, noisyCodeOnly);

        Assert.Equal(2, decisions.Count);

        var plain = Assert.Single(decisions, decision => decision.CommodityName == "*3455614");
        Assert.Equal("*3455614", plain.NormalizedName);

        var noisy = Assert.Single(decisions, decision => decision.CommodityName == "*  #3455614");
        Assert.Equal("* #3455614", noisy.NormalizedName);

        Assert.All(decisions, decision => Assert.False(string.IsNullOrWhiteSpace(decision.NormalizedName)));
        Assert.All(decisions, decision => Assert.True(decision.NormalizedName.Length <= MaxNameLength));
    }

    [Fact]
    public async Task RecordAsync_max_length_name_with_leading_code_is_written_with_shorter_normalized_name()
    {
        // Guard 256 проверяется по сырому имени; очистка только сокращает normalized_name.
        const string leadingCode = "2417268000006 ";
        var rawName = leadingCode + new string('Я', MaxNameLength - leadingCode.Length);

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), CreateDraft(rawName));

        var decision = Assert.Single(decisions);
        Assert.Equal(MaxNameLength, decision.CommodityName.Length);
        Assert.Equal(rawName, decision.CommodityName);
        Assert.True(decision.NormalizedName.Length <= MaxNameLength);
        Assert.Equal(new string('я', MaxNameLength - leadingCode.Length), decision.NormalizedName);
    }

    [Fact]
    public async Task RecordAsync_variants_with_and_without_code_share_normalized_name_but_not_raw_name()
    {
        // ADR-033 (ключ сочетания / E1): ключ остаётся по сырому имени, поэтому варианты
        // с кодом и без остаются двумя разными событиями, хотя очищенный ключ у них общий.
        var withCode = CreateDraft("2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ");
        var withoutCode = CreateDraft("ПИРОГ С ВИШНЕЙ ПО РЕ");

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), withCode, withoutCode);

        Assert.Equal(2, decisions.Count);
        Assert.Equal("пирог с вишней по ре", Assert.Single(decisions.Select(d => d.NormalizedName).Distinct()));
        Assert.Equal(2, decisions.Select(d => d.CommodityName).Distinct().Count());
        Assert.Contains(decisions, d => d.CommodityName == "2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ");
        Assert.Contains(decisions, d => d.CommodityName == "ПИРОГ С ВИШНЕЙ ПО РЕ");
    }

    [Theory]
    [InlineData(CategorizationDecisionSources.Ai)]
    [InlineData(CategorizationDecisionSources.Precategorized)]
    [InlineData(CategorizationDecisionSources.Cache)]
    [InlineData(CategorizationDecisionSources.MerchantDefault)]
    public async Task RecordAsync_each_allowed_source_is_written(string source)
    {
        var decisions = await RecordAndCaptureAsync(CreateRecorder(), CreateDraft(suggestionSource: source));

        var decision = Assert.Single(decisions);
        Assert.Equal(source, decision.SuggestionSource);
    }

    [Fact]
    public async Task RecordAsync_same_created_at_for_all_events_in_batch()
    {
        var decisions = await RecordAndCaptureAsync(
            CreateRecorder(),
            CreateDraft("Молоко"),
            CreateDraft("Кефир"));

        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, decision => Assert.Equal(Now, decision.CreatedAt));
    }

    [Fact]
    public async Task RecordAsync_non_admin_decision_preserves_admin_snapshot()
    {
        var decisions = await RecordAndCaptureAsync(CreateRecorder(), CreateDraft(acceptedByAdmin: false));

        var decision = Assert.Single(decisions);
        Assert.Equal(_userId, decision.AcceptedBy);
        Assert.False(decision.AcceptedByAdmin);
    }

    [Fact]
    public async Task RecordAsync_agreement_and_undefined_final_are_both_written()
    {
        // FR-3: согласия пишутся наравне с исправлениями; final = Undefined (0) — валидный сброс.
        var agreement = CreateDraft(final: CommodityCategory.Dairy);
        var reset = CreateDraft("Кефир", final: CommodityCategory.Undefined);

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), agreement, reset);

        Assert.Equal(2, decisions.Count);
        Assert.Contains(decisions, d => d.FinalCategory == CommodityCategory.Dairy);
        Assert.Contains(decisions, d => d.FinalCategory == CommodityCategory.Undefined);
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("undefined")]
    [InlineData("unknown-source")]
    [InlineData("AI")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RecordAsync_source_outside_allowed_set_is_skipped(string? source)
    {
        var invalid = CreateDraft(suggestionSource: source!);
        var valid = CreateDraft("Хлеб");

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), invalid, valid);

        var decision = Assert.Single(decisions);
        Assert.Equal("Хлеб", decision.CommodityName);
    }

    [Fact]
    public async Task RecordAsync_undefined_or_unknown_suggested_category_is_skipped()
    {
        var undefinedSuggested = CreateDraft(suggested: CommodityCategory.Undefined);
        var unknownSuggested = CreateDraft(suggested: (CommodityCategory)999);
        var unknownFinal = CreateDraft(final: (CommodityCategory)999);
        var valid = CreateDraft("Сыр");

        var decisions = await RecordAndCaptureAsync(
            CreateRecorder(),
            undefinedSuggested,
            unknownSuggested,
            unknownFinal,
            valid);

        var decision = Assert.Single(decisions);
        Assert.Equal("Сыр", decision.CommodityName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordAsync_empty_name_is_skipped(string name)
    {
        var decisions = await RecordAndCaptureAsync(CreateRecorder(), CreateDraft(name));

        Assert.Empty(decisions);
    }

    [Fact]
    public async Task RecordAsync_name_too_long_for_column_is_skipped()
    {
        var tooLong = CreateDraft(new string('я', MaxNameLength + 1));
        var maxLength = CreateDraft(new string('я', MaxNameLength));

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), tooLong, maxLength);

        var decision = Assert.Single(decisions);
        Assert.Equal(MaxNameLength, decision.CommodityName.Length);
    }

    [Fact]
    public async Task RecordAsync_empty_collection_does_not_call_repository()
    {
        var exception = await Record.ExceptionAsync(
            () => CreateRecorder().RecordAsync(Array.Empty<CategorizationDecisionDraft>(), CancellationToken.None));

        Assert.Null(exception);
        await _repository.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
    }

    [Fact]
    public async Task RecordAsync_all_invalid_drafts_do_not_call_repository()
    {
        var invalid = new[]
        {
            CreateDraft(suggestionSource: "existing"),
            CreateDraft(suggested: CommodityCategory.Undefined),
            CreateDraft("   "),
        };

        var exception = await Record.ExceptionAsync(
            () => CreateRecorder().RecordAsync(invalid, CancellationToken.None));

        Assert.Null(exception);
        await _repository.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
    }

    [Fact]
    public async Task RecordAsync_null_drafts_does_not_throw()
    {
        var exception = await Record.ExceptionAsync(
            () => CreateRecorder().RecordAsync(null!, CancellationToken.None));

        Assert.Null(exception);
        await _repository.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
    }

    [Fact]
    public async Task RecordAsync_batch_of_valid_drafts_uses_single_repository_call()
    {
        var drafts = new[]
        {
            CreateDraft("Молоко"),
            CreateDraft("Кефир"),
            CreateDraft("Сметана"),
        };

        var decisions = await RecordAndCaptureAsync(CreateRecorder(), drafts);

        Assert.Equal(3, decisions.Count);
        Assert.Equal(decisions.Count, decisions.Select(d => d.Id).Distinct().Count());
        await _repository.Received(1).AddRangeAsync(
            Arg.Any<IReadOnlyCollection<CategorizationDecision>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_repository_failure_is_swallowed()
    {
        _repository
            .AddRangeAsync(Arg.Any<IReadOnlyCollection<CategorizationDecision>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database is unavailable")));

        var exception = await Record.ExceptionAsync(
            () => CreateRecorder().RecordAsync([CreateDraft()], CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RecordAsync_cancellation_is_swallowed()
    {
        _repository
            .AddRangeAsync(Arg.Any<IReadOnlyCollection<CategorizationDecision>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException()));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exception = await Record.ExceptionAsync(
            () => CreateRecorder().RecordAsync([CreateDraft()], cancelled.Token));

        Assert.Null(exception);
    }

    private CategorizationDecisionDraft CreateDraft(
        string commodityName = "Молоко",
        string suggestionSource = CategorizationDecisionSources.Ai,
        CommodityCategory suggested = CommodityCategory.Dairy,
        CommodityCategory final = CommodityCategory.Bakery,
        bool acceptedByAdmin = true)
        => new(
            commodityName,
            suggestionSource,
            suggested,
            final,
            _userId,
            acceptedByAdmin);

    private async Task<IReadOnlyCollection<CategorizationDecision>> RecordAndCaptureAsync(
        ICategorizationDecisionRecorder recorder,
        params CategorizationDecisionDraft[] drafts)
    {
        IReadOnlyCollection<CategorizationDecision>? captured = null;
        _repository
            .AddRangeAsync(
                Arg.Do<IReadOnlyCollection<CategorizationDecision>>(decisions => captured = decisions),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await recorder.RecordAsync(drafts, CancellationToken.None);

        return captured ?? Array.Empty<CategorizationDecision>();
    }
}
