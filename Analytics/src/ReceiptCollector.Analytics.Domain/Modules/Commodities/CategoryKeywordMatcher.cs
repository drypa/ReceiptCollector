namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Единственная реализация матчинга ключевых слов (FR-6, FR-8, ADR-035 п. 4.1): чистая
/// статическая функция над снимком слов и уже нормализованным названием товара
/// (<see cref="CommodityNameNormalizer.NormalizeName"/>, ADR-034). Без БД, DI, логгера и
/// какого-либо состояния; доставка подсказок — только через
/// <c>ICommodityCategoryHintProvider</c> (задача 05).
/// </summary>
public static class CategoryKeywordMatcher
{
    /// <summary>
    /// Подсказки по совпавшим словам.
    /// </summary>
    /// <param name="snapshot">Снимок всех слов; порядок элементов не важен.</param>
    /// <param name="normalizedProductName">
    /// Название товара после <see cref="CommodityNameNormalizer.NormalizeName"/> — уже в нижнем
    /// регистре, поэтому дополнительная нормализация здесь не выполняется.
    /// </param>
    /// <returns>
    /// Все совпавшие подсказки (без усечения — лимит промта применяется на рендере, ADR-035 F1)
    /// в детерминированном порядке: exclude первыми, затем boost; внутри группы — код категории
    /// asc, затем <c>normalized_word</c> (ordinal) asc. Пустое/пробельное название или название
    /// без букв (например, из цифр) — пустой список.
    /// </returns>
    public static IReadOnlyList<CommodityCategoryKeyword> GetHints(
        CommodityCategoryKeywordSnapshot snapshot,
        string? normalizedProductName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(normalizedProductName) || !ContainsLetter(normalizedProductName))
        {
            return Array.Empty<CommodityCategoryKeyword>();
        }

        return snapshot.Items
            .Where(keyword => !string.IsNullOrEmpty(keyword.NormalizedWord)
                && normalizedProductName.Contains(keyword.NormalizedWord, StringComparison.Ordinal))
            .OrderBy(keyword => keyword.ListType == KeywordListType.Exclude ? 0 : 1)
            .ThenBy(keyword => (int)keyword.Category)
            .ThenBy(keyword => keyword.NormalizedWord, StringComparer.Ordinal)
            // Разрешение ничьих (полные дубли ключа) — только для стабильности вывода, если
            // снимок окажется частично неотсортированным: спецификация такие строки не различает.
            .ThenBy(keyword => keyword.Id)
            .ToList();
    }

    /// <summary>
    /// Есть ли в строке хотя бы одна буква (edge case задачи: название из цифр/мусора — совпадений
    /// не ищем). Проверка по Unicode-классу, а не по алфавиту: слова бывают латиницей.
    /// </summary>
    private static bool ContainsLetter(string value)
    {
        foreach (var symbol in value)
        {
            if (char.IsLetter(symbol))
            {
                return true;
            }
        }

        return false;
    }
}
