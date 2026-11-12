using System.Text;

namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Правила слова ключевых слов категоризации (FR-5, ADR-035 п. 1 и «Явно зафиксированные
/// семантики»): границы длины, лимит слов на список и единственная нормализация слова.
/// Правило применяется только к словам; нормализация названия товара —
/// <see cref="CommodityNameNormalizer"/> (ADR-034), к словам она не применяется.
/// </summary>
public static class CommodityCategoryKeywordRules
{
    /// <summary>
    /// Минимальная длина слова в символах (кодовых точках) после trim — FR-5; то же значение
    /// и та же единица измерения в CHECK <c>char_length(word) BETWEEN 2 AND 64</c> схемы.
    /// </summary>
    public const int MinWordLength = 2;

    /// <summary>
    /// Максимальная длина слова в символах (кодовых точках) после trim — FR-5; то же значение
    /// и та же единица измерения в CHECK <c>char_length(word) BETWEEN 2 AND 64</c> схемы.
    /// </summary>
    public const int MaxWordLength = 64;

    /// <summary>
    /// Предел слов на пару «категория + список» (FR-5, ADR-035 A5): продуктовое правило,
    /// проверяется сервисом по count перед вставкой; в схеме БД его нет.
    /// </summary>
    public const int MaxWordsPerList = 200;

    /// <summary>
    /// Нормализация слова — ключ case-insensitive уникальности (<c>normalized_word</c>) и вход
    /// подстрочного матчинга (FR-6): trim + lowercase invariant + схлопывание пробелов.
    /// Регистр самого слова (<c>word</c>) не меняется — он хранится как введено.
    /// </summary>
    public static string NormalizeWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return CollapseWhitespace(word.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Валидация слова по FR-5: после trim длина в границах
    /// <see cref="MinWordLength"/>…<see cref="MaxWordLength"/>. Пустое и пробельное слово
    /// отклоняется. Внутренние символы (кавычки, спецсимволы, эмодзи) допустимы; их
    /// нейтрализация — только на рендере промта (ADR-035 F1).
    /// </summary>
    /// <remarks>
    /// Длина считается в символах (кодовых точках Unicode), а не в UTF-16 code units:
    /// ровно так же считает CHECK <c>char_length(word) BETWEEN 2 AND 64</c> схемы (задача 01).
    /// Иначе слово из одного астрального символа (<c>"🙂"</c>) прошло бы валидацию и нарушило
    /// CHECK (23514) на вставке — это <c>500</c> вместо <c>400</c>, а слово из 33–64 символов
    /// вне BMP отклонялось бы, хотя схема его допускает.
    /// </remarks>
    public static bool IsValidWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        var length = CountRunes(word.Trim());
        return length >= MinWordLength && length <= MaxWordLength;
    }

    /// <summary>
    /// Схлопывание любых пробельных последовательностей в один пробел (без regex: разделитель
    /// <c>null</c> трактуется как любой <see cref="char.IsWhiteSpace(char)"/>).
    /// </summary>
    private static string CollapseWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Число символов (кодовых точек Unicode) — единица измерения FR-5 и CHECK схемы
    /// (<c>char_length</c>). Суррогатная пара считается одним символом.
    /// </summary>
    private static int CountRunes(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
