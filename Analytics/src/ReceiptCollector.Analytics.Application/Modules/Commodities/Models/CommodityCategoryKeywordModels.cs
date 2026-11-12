using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Категория со словами обоих списков для админского раздела (контракт
/// <c>GET /api/commodity-category-keywords</c>, ADR-035 п. 7, FR-3). Включает категории с
/// пустыми списками — плейсхолдеры и счётчики должны быть видны без второго запроса (UC-1).
/// </summary>
/// <param name="CategoryId">Числовой код <c>CommodityCategory</c> (без FK, ADR-005).</param>
/// <param name="CategoryKey">Имя элемента enum (например, <c>DriedFruits</c>) — стабильный ключ для UI.</param>
/// <param name="CategoryName">Отображаемое имя из <c>CommodityCategoryHelper.GetDisplayName</c>.</param>
/// <param name="Group">Группа из <c>CommodityCategoryHelper.GetGroup</c>; пустая строка — вне групп.</param>
/// <param name="Boost">Слова boost-списка, отсортированные по <c>word</c> (OrdinalIgnoreCase, затем Ordinal).</param>
/// <param name="Exclude">Слова exclude-списка в том же порядке.</param>
public sealed record CategoryKeywordsDto(
    int CategoryId,
    string CategoryKey,
    string CategoryName,
    string Group,
    IReadOnlyList<string> Boost,
    IReadOnlyList<string> Exclude);

/// <summary>
/// Исход добавления слова (ADR-035 п. 7). Невалидные категория/<c>listType</c>/слово в enum
/// не входят: на них эндпоинт отвечает <c>400</c> до обращения к сервису (тексты — таблица
/// ADR-035 п. 7); сервис повторяет те же проверки защитным <c>ArgumentException</c>.
/// </summary>
public enum AddKeywordOutcome
{
    /// <summary>Слово добавлено (commit состоялся, кэш проинвалидирован).</summary>
    Added,

    /// <summary>
    /// Такое же слово (case-insensitive по нормализации) уже есть в этой паре «категория +
    /// список» — предпроверка или гонка двух вкладок (unique violation 23505) → <c>409</c>.
    /// </summary>
    Duplicate,

    /// <summary>В списке уже <c>MaxWordsPerList</c> (200) слов → <c>409</c> (FR-5).</summary>
    LimitReached
}

/// <summary>
/// Результат добавления слова: при <see cref="AddKeywordOutcome.Added"/> несёт созданный элемент
/// для тела ответа <c>POST</c> (ADR-035 п. 7): <c>id</c> генерируется сервисом до вставки,
/// <c>word</c> — после trim в исходном регистре, <c>listType</c> — wire-имя.
/// При <see cref="AddKeywordOutcome.Duplicate"/>/<see cref="AddKeywordOutcome.LimitReached"/>
/// поля созданного элемента равны <c>null</c>.
/// </summary>
public sealed record AddKeywordResult(
    AddKeywordOutcome Outcome,
    Guid? Id = null,
    int? CategoryId = null,
    string? ListType = null,
    string? Word = null)
{
    public static AddKeywordResult Duplicate() => new(AddKeywordOutcome.Duplicate);

    public static AddKeywordResult LimitReached() => new(AddKeywordOutcome.LimitReached);

    public static AddKeywordResult Added(
        Guid id,
        int categoryId,
        string listType,
        string word) => new(AddKeywordOutcome.Added, id, categoryId, listType, word);
}

/// <summary>
/// Исход удаления слова по <c>id</c> (ADR-035 п. 7): <c>204</c> / <c>404 word not found.</c>
/// (строки нет — в том числе удалена в другой вкладке; удаление не влияет на уже проставленные
/// категории — ретроактивного пересчёта нет).
/// </summary>
public enum DeleteKeywordOutcome
{
    /// <summary>Строка удалена (commit состоялся, кэш проинвалидирован).</summary>
    Deleted,

    /// <summary>Строки с таким <c>id</c> нет → <c>404</c>.</summary>
    NotFound
}

/// <summary>
/// Подсказка по совпавшему ключевому слову для промта ИИ (ADR-035, решения C1/D1, FR-6/FR-8):
/// элемент результата <see cref="Contracts.ICommodityCategoryHintProvider.GetHints"/> — одно
/// совпавшее слово одной категории. Подсказки — данные запроса, а не инструкция: решение о
/// категории по-прежнему принимает модель (блок «Разграничения схожих категорий» остаётся до T-06).
/// Порядок списка (exclude первыми, затем boost) задаёт
/// <see cref="CategoryKeywordMatcher.GetHints"/> — получатели его не пересортировывают.
/// </summary>
/// <param name="Category">Категория слова (код <c>CommodityCategory</c>, не <c>Undefined</c> — запрещено схемой).</param>
/// <param name="ListType">Тип списка: boost («вероятно») или exclude («скорее не»); при конфликте exclude сильнее.</param>
/// <param name="Word">Слово как введено (FR-5): регистр и внутренние символы сохранены, обрамляющие пробелы сняты.</param>
public sealed record CommodityCategoryKeywordHint(
    CommodityCategory Category,
    KeywordListType ListType,
    string Word);
