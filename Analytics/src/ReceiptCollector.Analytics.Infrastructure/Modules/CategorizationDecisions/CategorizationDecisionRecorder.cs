using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;

/// <summary>
/// Best-effort запись журнала «предложено → принято» (ADR-033 п. 6, решение C1/NFR-3).
/// <para>
/// Единственная точка записи: валидирует и фильтрует черновики, нормализует название через
/// <see cref="CommodityNameNormalizer"/>, проставляет серверное время <see cref="TimeProvider"/>
/// и пишет все события одним <c>AddRangeAsync</c>. Метод никогда не бросает исключений:
/// сбой журнала логируется как <c>Error</c> и не влияет на пользовательское сохранение.
/// </para>
/// </summary>
internal sealed class CategorizationDecisionRecorder : ICategorizationDecisionRecorder
{
    /// <summary>Длина колонок <c>commodity_name</c>/<c>normalized_name</c> в схеме T-01.</summary>
    private const int MaxCommodityNameLength = 256;

    private readonly ICategorizationDecisionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CategorizationDecisionRecorder> _logger;

    public CategorizationDecisionRecorder(
        ICategorizationDecisionRepository repository,
        TimeProvider timeProvider,
        ILogger<CategorizationDecisionRecorder> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordAsync(
        IReadOnlyCollection<CategorizationDecisionDraft> drafts,
        CancellationToken cancellationToken = default)
    {
        // Контракт «никогда не бросает» покрывает всё тело метода: единственная операция вне try —
        // инициализация int-счётчика (не бросает), остальное, включая защиту от null-набора и
        // логирование, — внутри try (NFR-3, C1).
        var decisionCount = 0;

        try
        {
            if (drafts is null)
            {
                _logger.LogDebug("Categorization decision log: no drafts provided.");
                return;
            }

            decisionCount = drafts.Count;

            var createdAt = _timeProvider.GetUtcNow();
            var decisions = new List<CategorizationDecision>(drafts.Count);
            var skippedCount = 0;

            foreach (var draft in drafts)
            {
                if (!TryCreateDecision(draft, createdAt, out var decision))
                {
                    skippedCount++;
                    continue;
                }

                decisions.Add(decision);
            }

            decisionCount = decisions.Count;

            if (skippedCount > 0)
            {
                // ADR-033 п. 6: в Warning только количество — без названий и id (логи без ПД).
                _logger.LogWarning(
                    "Categorization decision log: {SkippedCount} drafts were skipped as invalid.",
                    skippedCount);
            }

            if (decisions.Count == 0)
            {
                _logger.LogDebug("Categorization decision log: no valid drafts to record.");
                return;
            }

            await _repository.AddRangeAsync(decisions, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Categorization decision log: {DecisionCount} events recorded.",
                decisions.Count);

            // Названия товаров — только Debug (логи без ПД, прецедент ADR-027/028).
            _logger.LogDebug(
                "Categorization decision log names: {CommodityNames}.",
                string.Join(" | ", decisions.Select(decision => decision.CommodityName)));
        }
        catch (Exception ex)
        {
            // C1/NFR-3: журнал наблюдательный — сбой, включая отмену токена, не выходит наружу.
            _logger.LogError(
                ex,
                "Categorization decision log: failed to record {DecisionCount} events.",
                decisionCount);
        }
    }

    /// <summary>
    /// Проверка формы одного черновика (ADR-033 п. 6): источник входит в четыре допустимых;
    /// обе категории — определённые коды (предложенная ≠ <c>Undefined</c>, итоговая
    /// <c>0</c> допустима — сброс в «Не указана»); имя непустое и вместе с нормализованным
    /// вариантом укладывается в длину колонки.
    /// </summary>
    private static bool TryCreateDecision(
        CategorizationDecisionDraft? draft,
        DateTimeOffset createdAt,
        [NotNullWhen(true)] out CategorizationDecision? decision)
    {
        decision = null;

        if (draft is null || !CategorizationDecisionSources.IsAllowed(draft.SuggestionSource))
        {
            return false;
        }

        if (!Enum.IsDefined(typeof(CommodityCategory), draft.SuggestedCategory)
            || draft.SuggestedCategory == CommodityCategory.Undefined
            || !Enum.IsDefined(typeof(CommodityCategory), draft.FinalCategory))
        {
            return false;
        }

        // Принятое уточнение T-03 (зафиксировано для приёмки T-12): commodity_name хранится после
        // Trim внешних пробелов; внутренние пробелы не трогаем — их схлопывает только
        // CommodityNameNormalizer. Так проверяемое значение гарантированно укладывается
        // в varchar(256) и не плодит сочетания-двойники из-за краевых пробелов.
        var commodityName = draft.CommodityName?.Trim();
        if (string.IsNullOrEmpty(commodityName) || commodityName.Length > MaxCommodityNameLength)
        {
            return false;
        }

        // Единственная нормализация имени — CommodityNameNormalizer (общий инвариант плана).
        // Lowercase может удлинить строку, поэтому нормализованное имя проверяется отдельно.
        var normalizedName = CommodityNameNormalizer.NormalizeName(commodityName);
        if (normalizedName.Length > MaxCommodityNameLength)
        {
            return false;
        }

        decision = new CategorizationDecision(
            Guid.NewGuid(),
            commodityName,
            normalizedName,
            draft.SuggestionSource,
            draft.SuggestedCategory,
            draft.FinalCategory,
            draft.AcceptedByUserId,
            draft.AcceptedByAdmin,
            createdAt);

        return true;
    }
}
