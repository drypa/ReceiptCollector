namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Слово ключевых слов категоризации (ADR-035 п. 1, решение A1): одна запись — одно слово
/// в одном списке одной категории. Категория — код enum без FK: справочника категорий в БД нет
/// (ADR-005), имена и группы считаются из <see cref="CommodityCategoryHelper"/> при чтении.
/// </summary>
/// <param name="Id">Идентификатор строки (адресное удаление <c>DELETE /{id}</c>, ADR-035 п. 7).</param>
/// <param name="Category">Категория слова; <see cref="CommodityCategory.Undefined"/> запрещён схемой.</param>
/// <param name="ListType">Тип списка (boost/exclude).</param>
/// <param name="Word">Слово как введено (FR-5): регистр и внутренние символы сохранены, обрамляющие пробелы сняты.</param>
/// <param name="NormalizedWord">
/// Ключ case-insensitive уникальности и вход подстрочного матчинга (FR-6):
/// <see cref="CommodityCategoryKeywordRules.NormalizeWord"/>.
/// </param>
public sealed record CommodityCategoryKeyword(
    Guid Id,
    CommodityCategory Category,
    KeywordListType ListType,
    string Word,
    string NormalizedWord);
