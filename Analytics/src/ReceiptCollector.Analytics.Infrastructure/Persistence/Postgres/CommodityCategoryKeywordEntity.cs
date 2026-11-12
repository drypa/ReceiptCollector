namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// EF-сущность слова категоризации (ADR-035 п. 1, решение A1): одна строка = одно слово
/// в одном списке одной категории. Query-filter не ставится: слова глобальные и общие
/// для всех пользователей (админское управление, ADR-035 п. 7).
/// </summary>
/// <remarks>
/// <see cref="CategoryId"/> — код <c>CommodityCategory</c> (enum в коде), FK осознанно нет:
/// справочника категорий в БД не существует (ADR-005). Имена и группы категорий считаются
/// <c>CommodityCategoryHelper</c> на чтении и в БД не денормализуются.
/// Тип списка хранится wire-именем (<c>boost</c>/<c>exclude</c>) в <see cref="ListType"/>:
/// доменный enum <c>KeywordListType</c> появляется в задаче 02, поэтому сущность намеренно
/// от него не зависит.
/// </remarks>
internal sealed class CommodityCategoryKeywordEntity
{
    public Guid Id { get; set; }

    /// <summary>Код <c>CommodityCategory</c>; <c>Undefined</c> (0) запрещён CHECK'ом.</summary>
    public int CategoryId { get; set; }

    /// <summary>Wire-имя списка: <c>boost</c> | <c>exclude</c> (varchar(16), CHECK).</summary>
    public string ListType { get; set; } = string.Empty;

    /// <summary>Слово как введено (FR-5), кроме обрамляющих пробелов (varchar(64)).</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>
    /// Ключ case-insensitive уникальности и вход подстрочного матчинга (FR-6):
    /// trim + lowercase invariant + collapse пробелов (varchar(64)).
    /// </summary>
    public string NormalizedWord { get; set; } = string.Empty;
}
