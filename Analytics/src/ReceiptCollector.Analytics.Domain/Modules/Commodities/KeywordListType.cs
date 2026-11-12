namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Тип списка ключевых слов категоризации (FR-1, ADR-035 п. 1): boost — слово повышает
/// вероятность категории («вероятно …»), exclude — понижает («скорее не …», FR-6/FR-7).
/// Множество закрыто требованием «два списка»: в БД тип хранится wire-именем
/// (<c>varchar(16)</c> + CHECK), числовой код enum в схему не попадает.
/// </summary>
public enum KeywordListType
{
    /// <summary>Слово повышает вероятность категории (wire-имя <c>boost</c>).</summary>
    Boost = 0,

    /// <summary>Слово исключает категорию (wire-имя <c>exclude</c>); при конфликте сильнее boost.</summary>
    Exclude = 1
}

/// <summary>
/// Wire-имена типов списков для контракта API и БД (<c>boost</c>/<c>exclude</c>, ADR-035 п. 1)
/// по образцу <c>CategorizationSourceNames</c>: имена в нижнем регистре, константы — единственный
/// источник строк в коде.
/// </summary>
public static class KeywordListTypeNames
{
    public const string Boost = "boost";
    public const string Exclude = "exclude";

    /// <summary>Wire-имя типа списка для ответа API и записи в БД.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Код вне <see cref="KeywordListType"/> (битое значение не должно молча превращаться в
    /// <c>boost</c> при записи в БД).
    /// </exception>
    public static string ToWireName(KeywordListType listType) => listType switch
    {
        KeywordListType.Boost => Boost,
        KeywordListType.Exclude => Exclude,
        _ => throw new ArgumentOutOfRangeException(
            nameof(listType),
            listType,
            "Unknown keyword list type.")
    };

    /// <summary>
    /// Разбор wire-имени (регистр не важен, обрамляющие пробелы не допускаются): <c>true</c> —
    /// значение распознано. Любая другая строка (<c>null</c>, пустая, числовой код, опечатка) —
    /// <c>false</c>: для админского API это <c>400 listType must be 'boost' or 'exclude'.</c>
    /// (ADR-035 п. 7).
    /// </summary>
    public static bool TryParse(string? value, out KeywordListType listType)
    {
        if (string.Equals(value, Boost, StringComparison.OrdinalIgnoreCase))
        {
            listType = KeywordListType.Boost;
            return true;
        }

        if (string.Equals(value, Exclude, StringComparison.OrdinalIgnoreCase))
        {
            listType = KeywordListType.Exclude;
            return true;
        }

        listType = default;
        return false;
    }
}
