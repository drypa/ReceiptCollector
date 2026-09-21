using System.Text.RegularExpressions;

namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Нормализация названий товаров для поиска совпадений в кэше ранее присвоенных категорий
/// (NFR-3.1 ADR 009): lowercase + trim + схлопывание пробелов.
/// </summary>
public static partial class CommodityNameNormalizer
{
    [GeneratedRegex(@"\s+", RegexOptions.None)]
    private static partial Regex WhitespaceRegex();

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim().ToLowerInvariant();
        return WhitespaceRegex().Replace(trimmed, " ");
    }
}