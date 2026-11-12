using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

// Wire-имена типа списка (ADR-035 п. 1): БД и API используют только boost/exclude.
public class KeywordListTypeNamesTests
{
    [Theory]
    [InlineData(KeywordListType.Boost, "boost")]
    [InlineData(KeywordListType.Exclude, "exclude")]
    public void ToWireName_returns_contract_value(KeywordListType listType, string expected)
    {
        Assert.Equal(expected, KeywordListTypeNames.ToWireName(listType));
    }

    [Theory]
    [InlineData("boost", KeywordListType.Boost)]
    [InlineData("exclude", KeywordListType.Exclude)]
    [InlineData("BOOST", KeywordListType.Boost)]
    [InlineData("Exclude", KeywordListType.Exclude)]
    public void TryParse_accepts_wire_names_regardless_of_case(string value, KeywordListType expected)
    {
        Assert.True(KeywordListTypeNames.TryParse(value, out var listType));
        Assert.Equal(expected, listType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" boost ")]
    [InlineData("0")]
    [InlineData("boosted")]
    [InlineData("other")]
    public void TryParse_rejects_values_outside_the_contract(string? value)
    {
        Assert.False(KeywordListTypeNames.TryParse(value, out _));
    }

    [Theory]
    [InlineData("boost")]
    [InlineData("exclude")]
    public void TryParse_and_ToWireName_round_trip(string wireName)
    {
        Assert.True(KeywordListTypeNames.TryParse(wireName, out var listType));
        Assert.Equal(wireName, KeywordListTypeNames.ToWireName(listType));
    }

    [Fact]
    public void ToWireName_fails_for_unknown_code_instead_of_silently_returning_boost()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeywordListTypeNames.ToWireName((KeywordListType)42));
    }
}
