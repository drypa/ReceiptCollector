using System.Collections.Frozen;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;

/// <summary>
/// Допустимые источники предложения в журнале «предложено → принято» (FR-1/FR-2, ADR-033
/// «Явно зафиксированные семантики»). Журнал фиксирует только предложения: <c>existing</c>
/// и <c>undefined</c> предложениями не являются и события не создают (FR-5b).
/// </summary>
public static class CategorizationDecisionSources
{
    /// <summary>Предложение ИИ.</summary>
    public const string Ai = CategorizationSourceNames.Ai;

    /// <summary>Предложение из очереди предкатегоризации.</summary>
    public const string Precategorized = CategorizationSourceNames.Precategorized;

    /// <summary>Предложение из in-memory кэша ранее подтверждённых категорий.</summary>
    public const string Cache = CategorizationSourceNames.Cache;

    /// <summary>Предложение — дефолт магазина чека (ADR-032).</summary>
    public const string MerchantDefault = CategorizationSourceNames.MerchantDefault;

    /// <summary>
    /// Полное допустимое множество wire-имён — ровно четыре источника (FR-1). Неизменяемое
    /// (<see cref="FrozenSet{T}"/>) с <see cref="StringComparer.Ordinal"/>: проверка — O(1),
    /// регистр и дефис в <c>merchant-default</c> значимы.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new[] { Ai, Precategorized, Cache, MerchantDefault }
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Принадлежность множеству — точное сравнение (<see cref="StringComparer.Ordinal"/>):
    /// регистр и дефис в <c>merchant-default</c> значимы.
    /// </summary>
    public static bool IsAllowed(string? source)
        => source is not null && All.Contains(source);
}

/// <summary>
/// Черновик события журнала, собранный save-путём (ADR-033 п. 6): снимок предложения от клиента
/// плюс серверные итог, принявший и admin-флаг. Валидацию выполняет recorder — вызывающий
/// (save-путь) черновики не фильтрует. Recorder по контракту не бросает; save-путь дополнительно
/// страхует пользовательское сохранение от нарушения этого контракта (NFR-3).
/// </summary>
public sealed record CategorizationDecisionDraft(
    string CommodityName,
    string SuggestionSource,
    CommodityCategory SuggestedCategory,
    CommodityCategory FinalCategory,
    Guid AcceptedByUserId,
    bool AcceptedByAdmin);

/// <summary>
/// Строка выдачи журнала — уникальное сочетание «название + источник + предложено + принято»
/// (FR-6/FR-7, ADR-033 п. 7). «Кто принял» и <see cref="DecidedAt"/> — от последнего события
/// сочетания внутри отфильтрованного периода.
/// <para>
/// Имена категорий — безопасное чтение (ADR-033 п. 1, прецедент ADR-032): известный код получает
/// <c>CommodityCategoryHelper.GetDisplayName</c>, удалённый из справочника — «—», при этом числовой
/// код в строке сохраняется. <see cref="AcceptedByName"/> равен <see langword="null"/>, если
/// пользователь удалён или имя пустое; <see cref="AcceptedById"/> и <see cref="AcceptedByAdmin"/>
/// заполнены всегда (F1).
/// </para>
/// </summary>
public sealed record CategorizationDecisionRow(
    string CommodityName,
    string NormalizedName,
    string SuggestionSource,
    int SuggestedCategoryId,
    string SuggestedCategoryName,
    int FinalCategoryId,
    string FinalCategoryName,
    Guid AcceptedById,
    string? AcceptedByName,
    bool AcceptedByAdmin,
    DateTimeOffset DecidedAt);
