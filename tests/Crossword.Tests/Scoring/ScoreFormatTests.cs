using System.Globalization;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Scoring;

/// <summary>Big numbers print in full while they fit, then as words — never scientific notation, never culture-dependent.</summary>
[Trait("Category", "Scoring")]
public class ScoreFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1,000")]
    [InlineData(123_456_789, "123,456,789")]
    [InlineData(1_234_567_890, "1,234,567,890")]
    [InlineData(999_999_999_999, "999,999,999,999")]
    [InlineData(1_000_000_000_000, "1 trillion")]
    [InlineData(1_234_000_000_000, "1.23 trillion")]
    [InlineData(12_345_000_000_000, "12.3 trillion")]
    [InlineData(123_456_000_000_000, "123 trillion")]
    [InlineData(999_950_000_000_000, "1 quadrillion")]
    [InlineData(1_000_000_000_000_000_000, "1 quintillion")]
    [InlineData(long.MaxValue, "9.22 quintillion")]
    [InlineData(-1_234_000_000_000, "-1.23 trillion")]
    public void Number_PrintsInFullUpToTwelveDigits_ThenInWords(long value, string expected) =>
        Assert.Equal(expected, ScoreFormat.Number(value));

    [Theory]
    [InlineData(999_999, 6, "999,999")]
    [InlineData(1_000_000, 6, "1 million")]
    [InlineData(1_500_000, 6, "1.5 million")]
    [InlineData(12_345, 3, "12.3 thousand")]
    [InlineData(1_234_567_890, 9, "1.23 billion")]
    [InlineData(1_234_567_890, 10, "1,234,567,890")]
    public void Number_HonoursTheLabelsDigitBudget(long value, int maxDigits, string expected) =>
        Assert.Equal(expected, ScoreFormat.Number(value, maxDigits));

    [Fact]
    public void Number_RoundsFractionsToTheNearestWholeNumber()
    {
        Assert.Equal("1,235", ScoreFormat.Number(1234.5m));
        Assert.Equal("1,234", ScoreFormat.Number(1234.4m));
        Assert.Equal("1.23 trillion", ScoreFormat.Number(1_234_000_000_000.9m));
    }

    [Theory]
    [InlineData(2.5, "2.5")]
    [InlineData(12.75, "12.75")]
    [InlineData(3, "3")]
    [InlineData(999.999, "1000")]
    [InlineData(1234.5, "1,234")]
    [InlineData(1_234_567_890_123.0, "1.23 trillion")]
    public void Mult_KeepsDecimalsWhileSmall_ThenGroupsLikeAScore(double value, string expected) =>
        Assert.Equal(expected, ScoreFormat.Mult((decimal)value));

    [Fact]
    public void Formatting_IgnoresTheCurrentCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1,234,567", ScoreFormat.Number(1_234_567));
            Assert.Equal("2.5", ScoreFormat.Mult(2.5m));
            Assert.Equal("1.23 trillion", ScoreFormat.Number(1_234_000_000_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
