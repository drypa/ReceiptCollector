namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Неизменяемый снимок всех слов категоризации (ADR-035 п. 4.1 и п. 6, решение E1): одно полное
/// чтение таблицы, дальше — только чтение из памяти без блокировок. Снимок целиком подменяется
/// кэшем после мутации; ни одна ссылка на снимок не меняется задним числом.
/// </summary>
/// <remarks>
/// Размещён в Domain (уточнение плана T-02), потому что вход матчера —
/// <see cref="CategoryKeywordMatcher.GetHints"/>: иначе Domain зависел бы от Application.
/// Коллекция копируется в конструкторе: снимок изолирован от изменений переданного списка
/// (иначе «неизменяемость» зависела бы от дисциплины вызывающего и конкурентное чтение
/// могло бы увидеть полуизменённый набор).
/// </remarks>
public sealed class CommodityCategoryKeywordSnapshot
{
    /// <summary>Пустой снимок: слов нет, совпадений не бывает (значение по умолчанию для кэша).</summary>
    public static CommodityCategoryKeywordSnapshot Empty { get; } =
        new(Array.Empty<CommodityCategoryKeyword>());

    public CommodityCategoryKeywordSnapshot(IReadOnlyList<CommodityCategoryKeyword> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.ToArray());
    }

    /// <summary>Все слова всех категорий и списков; порядок не гарантирован (его задаёт матчер).</summary>
    public IReadOnlyList<CommodityCategoryKeyword> Items { get; }
}
