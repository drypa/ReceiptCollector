using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Источник предложенной категории для товара
/// (известна из товара / дефолт магазина / из кэша / из очереди предкатегорий / от ИИ / не определена).
/// Порядок значений повторяет шаги конвейера (ADR-032, D1).
/// </summary>
public enum CategorizationSource
{
    Existing,

    /// <summary>
    /// Товарная категория по умолчанию магазина чека (ADR-032, D1): шаг между
    /// <see cref="Existing"/> и <see cref="Cache"/> (FR-M4).
    /// </summary>
    MerchantDefault,

    Cache,
    Precategorized,
    Ai,
    Undefined
}

/// <summary>Строковые значения источника категории для контракта API (lowercase, см. Unix-конвенцию фронта).</summary>
public static class CategorizationSourceNames
{
    public const string Existing = "existing";

    /// <summary>Wire-имя источника «дефолт магазина» (ADR-032, п. 2); аддитивное — существующие имена не изменены.</summary>
    public const string MerchantDefault = "merchant-default";

    public const string Cache = "cache";
    public const string Precategorized = "precategorized";
    public const string Ai = "ai";
    public const string Undefined = "undefined";

    public static string ToWireName(CategorizationSource source) => source switch
    {
        CategorizationSource.Existing => Existing,
        CategorizationSource.MerchantDefault => MerchantDefault,
        CategorizationSource.Cache => Cache,
        CategorizationSource.Precategorized => Precategorized,
        CategorizationSource.Ai => Ai,
        _ => Undefined
    };
}

/// <summary>
/// Фильтр списка товаров по категории (GET /api/commodities?categoryFilter=...).
/// </summary>
public enum CommodityCategoryFilter
{
    /// <summary>Без фильтра — все товары.</summary>
    Any = 0,

    /// <summary>Только товары без присвоенной категории (category_id IS NULL).</summary>
    Uncategorized = 1,

    /// <summary>
    /// Только товары без содержательной категории: «Не указана» — category_id IS NULL
    /// (миграция и сбросы пишут NULL) либо category_id = 0 (легаси Undefined).
    /// </summary>
    Undefined = 2
}

/// <summary>Результат категоризации всех товаров чека (suggest).</summary>
public sealed record ReceiptCategorizationResult(
    Guid ReceiptId,
    IReadOnlyCollection<ReceiptItemCategorizationResult> Items);

/// <summary>Результат категоризации одного товара чека. Source — строковое значение из <see cref="CategorizationSourceNames"/>.</summary>
public sealed record ReceiptItemCategorizationResult(
    Guid CommodityId,
    string Name,
    int? CategoryId,
    string? CategoryName,
    string Source,
    string? Error);

/// <summary>Ответ AI-модели: распознанная категория (имя enum, не числовой id).</summary>
public sealed record AiSuggestionResult(CommodityCategory Category);