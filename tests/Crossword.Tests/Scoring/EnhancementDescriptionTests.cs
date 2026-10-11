using Crossword.Core.Domain;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Scoring;

/// <summary>The player-facing text for Bold / Italic / Gilded comes from the config's numbers, in one place.</summary>
[Trait("Category", "Scoring")]
public class EnhancementDescriptionTests
{
    private static readonly ScoringConfig Config = ScoringConfig.Default with { BoldChips = 7, ItalicMult = 3, GildedMoney = 2 };

    [Theory]
    [InlineData(TileEnhancement.Bold, "Bold: +7 chips in every word it's in")]
    [InlineData(TileEnhancement.Italic, "Italic: +3 mult in every word it's in")]
    [InlineData(TileEnhancement.Gilded, "Gilded: +$2 in every word it's in")]
    [InlineData(TileEnhancement.None, "")]
    public void Describe_UsesTheConfigsNumbers(TileEnhancement enhancement, string expected) =>
        Assert.Equal(expected, Config.Describe(enhancement));

    [Fact]
    public void Describe_FormatsFractionalMultWithoutTrailingZeros() =>
        Assert.Equal("Italic: +1.5 mult in every word it's in", (Config with { ItalicMult = 1.5m }).Describe(TileEnhancement.Italic));

    [Fact]
    public void EnhancedOffers_ExplainTheEnhancement()
    {
        var addBold = new AddTileOffer(Letter.From('B'), TileEnhancement.Bold, 4);
        var enhance = new EnhanceOffer(TileEnhancement.Gilded, 3);

        Assert.Equal("Add Bold tile B to your deck — Bold: +7 chips in every word it's in", addBold.Describe(Config));
        Assert.Equal("Make one of your tiles Gilded — Gilded: +$2 in every word it's in", enhance.Describe(Config));
        Assert.DoesNotContain("chips", addBold.Description); // the plain description is unchanged (saves, bots)
    }

    [Fact]
    public void PlainAndWildOffers_KeepTheirDescription()
    {
        var plain = new AddTileOffer(Letter.From('B'), TileEnhancement.None, 2);
        var wild = new AddTileOffer(Letter.From('B'), TileEnhancement.Bold, 6, Wild: true);
        var strike = new StrikeOffer(2, 3);

        Assert.Equal(plain.Description, plain.Describe(Config));
        Assert.Equal(wild.Description, wild.Describe(Config));
        Assert.Equal(strike.Description, strike.Describe(Config));
    }
}
