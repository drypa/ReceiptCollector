using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityNameNormalizerTests
{
    [Theory]
    [InlineData("Молоко", "молоко")]
    [InlineData("  МОЛОКО  3.2%  ", "молоко 3.2%")]
    [InlineData("Чай\tЛиптон", "чай липтон")]
    [InlineData("Хлеб   Бородинский", "хлеб бородинский")]
    [InlineData("coca-cola", "coca-cola")]
    [InlineData("", "")]
    public void NormalizeName_lowercases_trims_and_collapses_whitespace(string input, string expected)
    {
        Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
    }

    [Fact]
    public void NormalizeName_keeps_each_punctuation_char_intact()
    {
        var input = "Пельмени «Домашние», 800 г";

        var result = CommodityNameNormalizer.NormalizeName(input);

        Assert.Equal("пельмени «домашние», 800 г", result);
    }

    [Fact]
    public void NormalizeName_distinguishes_similar_but_different_names()
    {
        var a = CommodityNameNormalizer.NormalizeName("Молоко 2.5%");
        var b = CommodityNameNormalizer.NormalizeName("Молоко 3.2%");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("Молоко 2.5%", "молоко   2.5%")]
    [InlineData("Кефир", " КЕФИР ")]
    public void NormalizeName_treats_variants_of_the_same_name_as_equal(string first, string second)
    {
        var a = CommodityNameNormalizer.NormalizeName(first);
        var b = CommodityNameNormalizer.NormalizeName(second);

        Assert.Equal(a, b);
    }

    [Fact]
    public void NormalizeName_throws_on_null()
    {
        Assert.Throws<ArgumentNullException>(() => CommodityNameNormalizer.NormalizeName(null!));
    }
}