using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

// Правила слова (FR-5, ADR-035 п. 1): нормализация ключа, границы длины и константы.
// Регистр самого слова (Word) не проверяется здесь — он сохраняется сервисом (задача 04).
public class CommodityCategoryKeywordRulesTests
{
    [Theory]
    [InlineData("  Курага  ", "курага")]
    [InlineData("СОЕВЫЙ   СОУС", "соевый соус")]
    [InlineData("Чай\tЛиптон", "чай липтон")]
    [InlineData("Хлеб\n\nБородинский", "хлеб бородинский")]
    [InlineData("соль", "соль")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeWord_trims_lowercases_and_collapses_whitespace(string input, string expected)
    {
        Assert.Equal(expected, CommodityCategoryKeywordRules.NormalizeWord(input));
    }

    [Fact]
    public void NormalizeWord_is_idempotent()
    {
        var normalized = CommodityCategoryKeywordRules.NormalizeWord("  СОЕВЫЙ   соус ");

        Assert.Equal(normalized, CommodityCategoryKeywordRules.NormalizeWord(normalized));
    }

    [Fact]
    public void IsValidWord_accepts_lengths_inside_bounds_and_rejects_outside()
    {
        Assert.False(CommodityCategoryKeywordRules.IsValidWord(new string('я', CommodityCategoryKeywordRules.MinWordLength - 1)));
        Assert.True(CommodityCategoryKeywordRules.IsValidWord(new string('я', CommodityCategoryKeywordRules.MinWordLength)));
        Assert.True(CommodityCategoryKeywordRules.IsValidWord(new string('я', CommodityCategoryKeywordRules.MaxWordLength)));
        Assert.False(CommodityCategoryKeywordRules.IsValidWord(new string('я', CommodityCategoryKeywordRules.MaxWordLength + 1)));
    }

    [Fact]
    public void IsValidWord_measures_length_after_trimming()
    {
        Assert.True(CommodityCategoryKeywordRules.IsValidWord("  соль  "));
        Assert.False(CommodityCategoryKeywordRules.IsValidWord("  я  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void IsValidWord_rejects_empty_and_whitespace_words(string? word)
    {
        Assert.False(CommodityCategoryKeywordRules.IsValidWord(word));
    }

    [Theory]
    [InlineData("!?")]
    [InlineData("соль!")]
    [InlineData("«курага»")]
    [InlineData("🙂🙂")]
    [InlineData("coca-cola")]
    public void IsValidWord_accepts_special_symbols_and_emoji_within_length_bounds(string word)
    {
        Assert.True(CommodityCategoryKeywordRules.IsValidWord(word));
    }

    // Длина слова измеряется в символах (кодовых точках Unicode), как CHECK
    // char_length(word) BETWEEN 2 AND 64 схемы задачи 01. Поэтому "🙂" — 1 символ
    // (2 UTF-16 code units) и невалиден.
    [Fact]
    public void IsValidWord_counts_characters_not_utf16_code_units()
    {
        Assert.False(CommodityCategoryKeywordRules.IsValidWord("🙂"));
        Assert.True(CommodityCategoryKeywordRules.IsValidWord(EmojiWord(CommodityCategoryKeywordRules.MinWordLength)));
        Assert.True(CommodityCategoryKeywordRules.IsValidWord(EmojiWord(CommodityCategoryKeywordRules.MaxWordLength)));
        Assert.False(CommodityCategoryKeywordRules.IsValidWord(EmojiWord(CommodityCategoryKeywordRules.MaxWordLength + 1)));
    }

    private static string EmojiWord(int characters) => string.Concat(Enumerable.Repeat("🙂", characters));

    [Fact]
    public void Constants_match_the_contract()
    {
        Assert.Equal(2, CommodityCategoryKeywordRules.MinWordLength);
        Assert.Equal(64, CommodityCategoryKeywordRules.MaxWordLength);
        Assert.Equal(200, CommodityCategoryKeywordRules.MaxWordsPerList);
    }
}
