using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

// Матчер подсказок (FR-6, FR-8, ADR-035 п. 4.1): чистая функция над снимком слов и уже
// нормализованным названием товара. Порядок: exclude → boost, внутри — код категории asc,
// затем normalized_word (ordinal) asc; результат не усекается.
public class CategoryKeywordMatcherTests
{
    [Fact]
    public void GetHints_matches_substring_when_stored_word_has_different_case()
    {
        var snapshot = Snapshot(Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "Соль"));

        var hints = CategoryKeywordMatcher.GetHints(snapshot, "фасоль");

        var hint = Assert.Single(hints);
        Assert.Equal(CommodityCategory.Groceries, hint.Category);
        Assert.Equal("соль", hint.NormalizedWord);
        Assert.Equal("Соль", hint.Word);
    }

    [Fact]
    public void GetHints_matches_word_inside_longer_word_without_word_boundaries()
    {
        var snapshot = Snapshot(Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль"));

        Assert.Single(CategoryKeywordMatcher.GetHints(snapshot, "фасоль красная"));
        Assert.Empty(CategoryKeywordMatcher.GetHints(snapshot, "сахар"));
    }

    // Контракт матчера: на вход приходит УЖЕ нормализованное название (CommodityNameNormalizer).
    // Поэтому название в верхнем регистре не совпадает — матчер не нормализует его повторно.
    [Fact]
    public void GetHints_does_not_lowercase_the_product_name()
    {
        var snapshot = Snapshot(Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль"));

        Assert.Single(CategoryKeywordMatcher.GetHints(snapshot, "фасоль"));
        Assert.Empty(CategoryKeywordMatcher.GetHints(snapshot, "ФАСОЛЬ"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("12345")]
    [InlineData(" 42 7 ")]
    [InlineData("---")]
    public void GetHints_returns_empty_for_name_without_letters(string name)
    {
        var snapshot = Snapshot(Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль"));

        Assert.Empty(CategoryKeywordMatcher.GetHints(snapshot, name));
    }

    [Fact]
    public void GetHints_returns_empty_for_empty_snapshot()
    {
        Assert.Empty(CategoryKeywordMatcher.GetHints(CommodityCategoryKeywordSnapshot.Empty, "хлеб бородинский"));
    }

    // Защитный фильтр: пустой normalized_word не должен совпадать с любым названием
    // (string.Contains("") == true). Схема такие строки не допускает — тест фиксирует намерение.
    [Fact]
    public void GetHints_ignores_keyword_with_empty_normalized_word()
    {
        var snapshot = Snapshot(
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.Groceries, KeywordListType.Boost, "", ""),
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "хлеб"));

        var hint = Assert.Single(CategoryKeywordMatcher.GetHints(snapshot, "хлеб бородинский"));
        Assert.Equal("хлеб", hint.NormalizedWord);
    }

    [Fact]
    public void Snapshot_is_isolated_from_changes_of_the_source_list()
    {
        var source = new List<CommodityCategoryKeyword>
        {
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль")
        };
        var snapshot = new CommodityCategoryKeywordSnapshot(source);

        source.Clear();

        Assert.Single(snapshot.Items);
    }

    [Fact]
    public void GetHints_returns_both_boost_hints_when_word_matches_two_categories()
    {
        var snapshot = Snapshot(
            Keyword(CommodityCategory.Sweets, KeywordListType.Boost, "шоколад"),
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "шоколад"));

        var hints = CategoryKeywordMatcher.GetHints(snapshot, "шоколад молочный");

        Assert.Equal(2, hints.Count);
        Assert.All(hints, hint => Assert.Equal(KeywordListType.Boost, hint.ListType));
        Assert.Equal(
            new[] { CommodityCategory.Groceries, CommodityCategory.Sweets }.OrderBy(category => (int)category),
            hints.Select(hint => hint.Category));
    }

    [Fact]
    public void GetHints_puts_exclude_before_boost_for_the_same_category()
    {
        var snapshot = Snapshot(
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль"),
            Keyword(CommodityCategory.Groceries, KeywordListType.Exclude, "соль"));

        var hints = CategoryKeywordMatcher.GetHints(snapshot, "фасоль");

        Assert.Equal(2, hints.Count);
        Assert.Equal(KeywordListType.Exclude, hints[0].ListType);
        Assert.Equal(KeywordListType.Boost, hints[1].ListType);
    }

    [Fact]
    public void GetHints_puts_exclude_first_even_when_its_category_code_is_greater()
    {
        var snapshot = Snapshot(
            Keyword(CommodityCategory.Food, KeywordListType.Boost, "изюм"),
            Keyword(CommodityCategory.DriedFruits, KeywordListType.Exclude, "изюм"));

        var hints = CategoryKeywordMatcher.GetHints(snapshot, "изюм 250 г");

        Assert.Equal(2, hints.Count);
        Assert.Equal(KeywordListType.Exclude, hints[0].ListType);
        Assert.Equal(CommodityCategory.DriedFruits, hints[0].Category);
        Assert.Equal(KeywordListType.Boost, hints[1].ListType);
        Assert.Equal(CommodityCategory.Food, hints[1].Category);
    }

    [Fact]
    public void GetHints_orders_by_category_then_normalized_word()
    {
        var snapshot = Snapshot(
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "яблоко"),
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "абрикос"),
            Keyword(CommodityCategory.Dairy, KeywordListType.Boost, "яблоко"),
            Keyword(CommodityCategory.Dairy, KeywordListType.Boost, "абрикос"));

        var hints = CategoryKeywordMatcher.GetHints(snapshot, "абрикос яблоко");

        Assert.Equal(4, hints.Count);
        Assert.Equal(
            new[]
            {
                (CommodityCategory.Dairy, "абрикос"),
                (CommodityCategory.Dairy, "яблоко"),
                (CommodityCategory.Groceries, "абрикос"),
                (CommodityCategory.Groceries, "яблоко")
            }.OrderBy(item => (int)item.Item1).ThenBy(item => item.Item2, StringComparer.Ordinal),
            hints.Select(hint => (hint.Category, hint.NormalizedWord)));
    }

    [Fact]
    public void GetHints_returns_same_order_for_reversed_snapshot()
    {
        var keywords = new[]
        {
            Keyword(CommodityCategory.Groceries, KeywordListType.Boost, "соль"),
            Keyword(CommodityCategory.Groceries, KeywordListType.Exclude, "сахар"),
            Keyword(CommodityCategory.Sweets, KeywordListType.Exclude, "соль"),
            Keyword(CommodityCategory.Dairy, KeywordListType.Boost, "масло"),
            Keyword(CommodityCategory.Sweets, KeywordListType.Boost, "сахар")
        };

        var direct = CategoryKeywordMatcher.GetHints(Snapshot(keywords), "соль сахар масло");
        var reversed = CategoryKeywordMatcher.GetHints(Snapshot(keywords.Reverse().ToArray()), "соль сахар масло");

        Assert.Equal(direct.Count, reversed.Count);
        Assert.Equal(direct.Select(hint => hint.Id), reversed.Select(hint => hint.Id));
    }

    [Fact]
    public void GetHints_does_not_truncate_two_hundred_matched_words_across_categories()
    {
        var words = Enumerable.Range(0, CommodityCategoryKeywordRules.MaxWordsPerList)
            .Select(index => $"слово{index}")
            .ToArray();
        var keywords = words
            .Select(word => Keyword(CommodityCategory.Groceries, KeywordListType.Boost, word))
            .Concat(words.Select(word => Keyword(CommodityCategory.Sweets, KeywordListType.Boost, word)))
            .ToArray();

        var hints = CategoryKeywordMatcher.GetHints(Snapshot(keywords), string.Join(' ', words));

        Assert.Equal(2 * CommodityCategoryKeywordRules.MaxWordsPerList, hints.Count);
        Assert.Equal(hints.Count, hints.Select(hint => hint.Id).Distinct().Count());
        Assert.Equal(
            hints.Select(hint => (int)hint.Category),
            hints.Select(hint => (int)hint.Category).OrderBy(category => category));
    }

    private static CommodityCategoryKeywordSnapshot Snapshot(params CommodityCategoryKeyword[] items) => new(items);

    private static CommodityCategoryKeyword Keyword(CommodityCategory category, KeywordListType listType, string word)
        => new(
            Guid.NewGuid(),
            category,
            listType,
            word,
            CommodityCategoryKeywordRules.NormalizeWord(word));
}
