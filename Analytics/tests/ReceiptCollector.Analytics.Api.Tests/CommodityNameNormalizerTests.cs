using System.Globalization;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityNameNormalizerTests
{
    [Theory]
    [InlineData("Молоко", "молоко")]
    [InlineData("Чай\tЛиптон", "чай липтон")]
    [InlineData("Хлеб   Бородинский", "хлеб бородинский")]
    [InlineData("coca-cola", "coca-cola")]
    [InlineData("", "")]
    public void NormalizeName_lowercases_trims_and_collapses_whitespace(string input, string expected)
    {
        Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
    }

    // Каноническое правило очистки — golden corpus (таблица примеров ADR-034 п. 1, границы
    // и документированные ограничения). Единственный источник кейсов — CommodityNameNormalizationCorpus,
    // тот же corpus проверяет SQL-backfill миграции (PrecategorizedNormalizedNameMigrationTests).
    [Theory]
    [MemberData(nameof(CommodityNameNormalizationCorpus.TheoryCases), MemberType = typeof(CommodityNameNormalizationCorpus))]
    public void NormalizeName_applies_canonical_cleaning_rule(string input, string expected)
    {
        Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
    }

    [Fact]
    public void NormalizeName_is_deterministic_for_the_same_input()
    {
        const string input = "2195738 Туалетная бумага \"СВАЁ\" (3сл, 4р) 4810981001530";
        var expected = CommodityNameNormalizer.NormalizeName(input);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
        }

        Assert.Equal("туалетная бумага \"сваё\" (3сл, 4р)", expected);
    }

    [Fact]
    public void NormalizeName_does_not_depend_on_current_culture()
    {
        // Нормализация обязана использовать ToLowerInvariant: под турецкой культурой
        // ToLower() отобразил бы "I" в "ı", а "İ" в "i", и ключи разошлись бы между сервисами.
        const string input = "2417268000006 IPHONE IİI ıI КЕФИР";
        var expected = CommodityNameNormalizer.NormalizeName(input);

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal("iphone хлеб", CommodityNameNormalizer.NormalizeName("2417268000006 IPHONE ХЛЕБ"));
            Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
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
