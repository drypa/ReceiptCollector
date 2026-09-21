using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Источник предложенной категории для товара (известна из товара / из кэша / от ИИ / не определена).
/// </summary>
public enum CategorizationSource
{
    Existing,
    Cache,
    Ai,
    Undefined
}

/// <summary>Строковые значения источника категории для контракта API (lowercase, см. Unix-конвенцию фронта).</summary>
public static class CategorizationSourceNames
{
    public const string Existing = "existing";
    public const string Cache = "cache";
    public const string Ai = "ai";
    public const string Undefined = "undefined";

    public static string ToWireName(CategorizationSource source) => source switch
    {
        CategorizationSource.Existing => Existing,
        CategorizationSource.Cache => Cache,
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

    /// <summary>Только товары с категорией «Не указана» (category_id = 0, Undefined).</summary>
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