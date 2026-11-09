using System.Text.RegularExpressions;

namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Нормализация названий товаров — единственное правило формирования ключа
/// (ADR 009, NFR-3.1; очистка усилена в ADR-034 п. 1):
/// lowercase + trim + схлопывание пробелов, снятие ведущего мусора (<c>*</c>, <c>#</c>, <c>№</c>)
/// и непрерывных цифровых кодов от 6 цифр в начале и в конце названия.
/// </summary>
/// <remarks>
/// <para>Шаги правила (ADR-034 п. 1):</para>
/// <list type="number">
///   <item><description><c>base</c> — <c>CollapseWhitespace(name.Trim().ToLowerInvariant())</c>;</description></item>
///   <item><description>снятие ведущих пробелов/мусора и цифровых кодов (<c>^(?:[\s*#№]+|[0-9]{6,})+</c>);</description></item>
///   <item><description>снятие хвостового кластера кодов (<c>(?:^|\s)[0-9]{6,}(?:\s+[0-9]{6,})*\s*$</c>);</description></item>
///   <item><description>финальные trim и схлопывание пробелов;</description></item>
///   <item><description>если результат пуст или в нём нет ни одной буквы — возвращается <c>base</c>.</description></item>
/// </list>
/// <para>
/// Fallback на <c>base</c> не допускает пустой ключ для имён, состоящих только из кода/пунктуации
/// (<c>2417268000006</c>, <c>*3455614</c>). Функция детерминирована и не имеет побочных эффектов
/// (NFR-3).
/// </para>
/// </remarks>
public static partial class CommodityNameNormalizer
{
    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:[\s*#№]+|[0-9]{6,})+", RegexOptions.None)]
    private static partial Regex LeadingNoiseAndCodeRegex();

    [GeneratedRegex(@"(?:^|\s)[0-9]{6,}(?:\s+[0-9]{6,})*\s*$", RegexOptions.None)]
    private static partial Regex TrailingCodeClusterRegex();

    [GeneratedRegex(@"[A-Za-zА-Яа-яЁё]", RegexOptions.None)]
    private static partial Regex LetterRegex();

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var baseName = CollapseWhitespace(name.Trim().ToLowerInvariant());

        var cleaned = LeadingNoiseAndCodeRegex().Replace(baseName, string.Empty);
        cleaned = TrailingCodeClusterRegex().Replace(cleaned, string.Empty);
        cleaned = CollapseWhitespace(cleaned.Trim());

        return cleaned.Length == 0 || !LetterRegex().IsMatch(cleaned)
            ? baseName
            : cleaned;
    }

    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value, " ");
}
