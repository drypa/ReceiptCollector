using System.Globalization;
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

    // Каноническое правило очистки — таблица примеров ADR-034 п. 1 (все строки, включая
    // документированные ограничения: дефис внутри номера и кластер через запятую).
    [Theory]
    // Четыре примера из задачи.
    [InlineData("2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ", "пирог с вишней по ре")]
    [InlineData("2444436000001 СЕТ ДУЭТ 256г", "сет дуэт 256г")]
    [InlineData("*3455614 РЖ.КРАЙ Хлеб нар.300г", "рж.край хлеб нар.300г")]
    [InlineData(
        "2195738 Туалетная бумага \"СВАЁ\" (3сл, 4р) 4810981001530",
        "туалетная бумага \"сваё\" (3сл, 4р)")]
    // Короткие числа и бренд не снимаются.
    [InlineData("АИ-95", "аи-95")]
    [InlineData("1000 ЛЕПЕСТКОВ", "1000 лепестков")]
    [InlineData("256г", "256г")]
    [InlineData("нар.300г", "нар.300г")]
    [InlineData("(3сл, 4р)", "(3сл, 4р)")]
    [InlineData("РЖ.КРАЙ", "рж.край")]
    // Имя состоит только из мусора/кода — fallback на нормализованный оригинал.
    [InlineData("*3455614", "*3455614")]
    [InlineData("2417268000006", "2417268000006")]
    [InlineData("*345", "*345")]
    // Ведущий мусор и код снимаются полностью.
    [InlineData("№ 2417268000006 хлеб", "хлеб")]
    [InlineData("**##№№ 2417268000006 хлеб", "хлеб")]
    // Хвостовой код и кластер кодов через пробел.
    [InlineData("пирог 123456", "пирог")]
    [InlineData("пирог 12345", "пирог 12345")]
    [InlineData("пирог 123456 789012", "пирог")]
    // Документированные ограничения правила: дефис и запятая внутри номера не распознаются.
    [InlineData("241-7268000006 хлеб", "241-7268000006 хлеб")]
    [InlineData("хлеб 241-7268000006", "хлеб 241-7268000006")]
    [InlineData("хлеб 123456, 789012", "хлеб 123456,")]
    // Хвостовой мусор не снимается; 5-значный сосед не мешает снятию хвостового кода.
    [InlineData("хлеб *", "хлеб *")]
    [InlineData("хлеб 12345 789012", "хлеб 12345")]
    public void NormalizeName_applies_canonical_cleaning_rule(string input, string expected)
    {
        Assert.Equal(expected, CommodityNameNormalizer.NormalizeName(input));
    }

    [Theory]
    [InlineData("хлеб 123456 пирог")]
    [InlineData("хлеб 2417268000006 пирог с вишней")]
    public void NormalizeName_does_not_strip_code_in_the_middle(string input)
    {
        var result = CommodityNameNormalizer.NormalizeName(input);

        Assert.Equal(input, result);
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
