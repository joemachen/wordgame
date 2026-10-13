using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

/// <summary>The compounding batch (2026-10-12). Every number is pinned here, not read from the items' defaults.</summary>
[Trait("Category", "Scoring")]
public class CompoundingDeskItemsTests
{
    // CAT across on an empty board: 1 word, 3 tiles, no intersections.
    private static readonly PlayAnalysis SimplePlay = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

    // TO under AT in CAT: words TO, AT, TO — 3 words, 2 tiles, both intersections.
    private static readonly PlayAnalysis ParallelPlay =
        PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");

    private static readonly ScoringConfig Pinned = ScoringConfig.Default with
    {
        BoldChips = 10, ItalicMult = 2, GildedMoney = 1, UnderlinedMult = 1.5m, EmbossedChips = 2m,
    };

    private static ScoreContext Start(PlayAnalysis play, ScoreEnvironment? env = null) =>
        ScoreContext.Start(play, chips: 10, mult: 2) with { Env = env ?? ScoreEnvironment.Empty, Scoring = Pinned };

    private static void AssertNoEffect(IDeskItem item, ScoreContext start) => Assert.Same(start, item.Apply(start));

    /// <summary>CAT spelled from hand tiles C, A, T with the given enhancements.</summary>
    private static PlayAnalysis CatWith(TileEnhancement c, TileEnhancement a = TileEnhancement.None, TileEnhancement t = TileEnhancement.None)
    {
        var hand = new Hand([new Tile(0, Letter.From('C'), c), new Tile(1, Letter.From('A'), a), new Tile(2, Letter.From('T'), t)]);
        var result = PlacementValidator.Validate(Board.Empty(5), hand, Spell(Board.Empty(5), hand, 0, 0, Direction.Across, "CAT"), Words);
        Assert.True(result.IsOk);
        return result.Value;
    }

    [Fact]
    public void Compound_RaisesToAPower_InDecimal()
    {
        Assert.Equal(3.375m, Compound.Power(1.5m, 3));
        Assert.Equal(1m, Compound.Power(1.5m, 0));
        Assert.Equal(ScoreMath.MaxMult, Compound.Power(10m, 40));
    }

    [Fact]
    public void ColumnInches_MultipliesMult_PerWord()
    {
        Assert.Equal(3m, new ColumnInches(PerWord: 1.5m).Apply(Start(SimplePlay)).Mult);
        Assert.Equal(2 * 3.375m, new ColumnInches(PerWord: 1.5m).Apply(Start(ParallelPlay)).Mult);
        Assert.Equal(10, new ColumnInches(PerWord: 1.5m).Apply(Start(ParallelPlay)).Chips);
    }

    [Fact]
    public void HotType_MultipliesMult_PerTilePlaced()
    {
        Assert.Equal(2 * 1.728m, new HotType(PerTile: 1.2m).Apply(Start(SimplePlay)).Mult);
        Assert.Equal(2 * 1.44m, new HotType(PerTile: 1.2m).Apply(Start(ParallelPlay)).Mult);
    }

    [Fact]
    public void Interlock_MultipliesMult_PerIntersection()
    {
        AssertNoEffect(new Interlock(PerIntersection: 1.4m), Start(SimplePlay));
        Assert.Equal(2 * 1.96m, new Interlock(PerIntersection: 1.4m).Apply(Start(ParallelPlay)).Mult);
    }

    [Fact]
    public void EveningEdition_MultipliesMult_PerPlayAlreadyMade()
    {
        AssertNoEffect(new EveningEdition(PerSubmission: 1.5m), Start(SimplePlay, new ScoreEnvironment(0, 4, 3) { SubmissionsMade = 0 }));
        Assert.Equal(2 * 2.25m, new EveningEdition(PerSubmission: 1.5m).Apply(Start(SimplePlay, new ScoreEnvironment(0, 2, 3) { SubmissionsMade = 2 })).Mult);
    }

    [Fact]
    public void FeatureStory_MultipliesMult_ByLettersBeyondTheThreshold()
    {
        var star = PlayOn(Board.Empty(5), "STAR", 0, 0, Direction.Across, "STAR");

        AssertNoEffect(new FeatureStory(PerLetter: 0.5m, BeyondLength: 4), Start(star));
        AssertNoEffect(new FeatureStory(PerLetter: 0.5m, BeyondLength: 3), Start(SimplePlay));
        Assert.Equal(3m, new FeatureStory(PerLetter: 0.5m, BeyondLength: 3).Apply(Start(star)).Mult);
        Assert.Equal(4m, new FeatureStory(PerLetter: 0.5m, BeyondLength: 2).Apply(Start(star)).Mult);
    }

    [Fact]
    public void AdRevenue_MultipliesMult_ByMoneyHeld()
    {
        AssertNoEffect(new AdRevenue(PerStep: 0.1m, MoneyStep: 5), Start(SimplePlay, new ScoreEnvironment(4, 4, 3)));
        Assert.Equal(2.8m, new AdRevenue(PerStep: 0.1m, MoneyStep: 5).Apply(Start(SimplePlay, new ScoreEnvironment(23, 4, 3))).Mult);
    }

    [Fact]
    public void SubscriptionDrive_CompoundsWithEveryMultiWordPlay()
    {
        var fresh = new SubscriptionDrive(Growth: 1.05m, MinWords: 3);
        AssertNoEffect(fresh, Start(SimplePlay));
        Assert.Same(fresh, fresh.AfterPlay(SimplePlay));

        var grown = (SubscriptionDrive)fresh.AfterPlay(ParallelPlay);
        Assert.Equal(1.05m, grown.Factor);
        Assert.Equal(2 * 1.05m, grown.Apply(Start(SimplePlay)).Mult);
        Assert.Equal(1.1025m, ((SubscriptionDrive)grown.AfterPlay(ParallelPlay)).Factor);
    }

    [Fact]
    public void Newsstand_MultipliesChips_PerWord()
    {
        var one = new Newsstand(PerWord: 0.25m).Apply(Start(SimplePlay));
        var three = new Newsstand(PerWord: 0.25m).Apply(Start(ParallelPlay));

        Assert.Equal((12L, 2m), (one.Chips, one.Mult)); // floor(10 × 1.25)
        Assert.Equal((17L, 2m), (three.Chips, three.Mult)); // floor(10 × 1.75)
    }

    [Theory]
    [InlineData(TileEnhancement.Bold, 20, 2, 0)]
    [InlineData(TileEnhancement.Italic, 10, 4, 0)]
    [InlineData(TileEnhancement.Gilded, 10, 2, 1)]
    [InlineData(TileEnhancement.Underlined, 10, 3, 0)]
    [InlineData(TileEnhancement.Embossed, 20, 2, 0)]
    public void PullQuote_FiresTheLongestWordsEnhancementsAgain(TileEnhancement enhancement, long chips, decimal mult, int money)
    {
        var after = new PullQuote().Apply(Start(CatWith(enhancement)));

        Assert.Equal((chips, mult, money), (after.Chips, after.Mult, after.Money));
        Assert.Equal("pull-quote", after.Log.Single().SourceId);
    }

    [Fact]
    public void PullQuote_AddsBeforeItMultiplies_AndIgnoresPlainWords()
    {
        var both = new PullQuote().Apply(Start(CatWith(TileEnhancement.Italic, t: TileEnhancement.Underlined)));
        Assert.Equal((2 + 2) * 1.5m, both.Mult);

        AssertNoEffect(new PullQuote(), Start(SimplePlay));
        // Enhanced tiles only in a cross word that isn't the longest: nothing fires.
        var board = Enhance(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), 0, 1, TileEnhancement.Bold);
        var longerMain = PlayOn(board, "TOE", 1, 1, Direction.Across, "TOE"); // TOE across (3) beats AT (2)
        AssertNoEffect(new PullQuote(), Start(longerMain));
    }

    [Fact]
    public void CarbonCopy_FiresTheStrongestMultiplierAgain()
    {
        IDeskItem[] desk = [new RedPen(2), new Pulitzer(Factor: 2, PerBoss: 0), new SecondPrinting(2), new CarbonCopy()];

        var scored = EffectPipeline.Apply(desk, Start(SimplePlay));

        // 10 × 2 → Red Pen 10 × 4 → Pulitzer 10 × 8 → Second Printing 20 × 16 → Carbon Copy copies Second Printing (×4 total beats ×2).
        Assert.Equal((40L, 32m), (scored.Chips, scored.Mult));
        Assert.Equal("Carbon Copy: Second Printing again", scored.Log.Last().Description);
        Assert.Equal("carbon-copy", scored.Log.Last().SourceId);
    }

    [Fact]
    public void CarbonCopy_DoesNothing_WithoutAMultiplier_OrAlone()
    {
        var start = Start(SimplePlay);

        Assert.Equal((10L, 4m), (EffectPipeline.Apply([new RedPen(2), new CarbonCopy()], start).Chips, EffectPipeline.Apply([new RedPen(2), new CarbonCopy()], start).Mult));
        Assert.DoesNotContain(EffectPipeline.Apply([new RedPen(2), new CarbonCopy()], start).Log, e => e.SourceId == "carbon-copy");
        AssertNoEffect(new CarbonCopy(), start);
        Assert.Same(start, new CarbonCopy().Apply(start, [new CarbonCopy()]));
        Assert.Same(start, new CarbonCopy().Apply(start, [new Interlock()])); // a multiplier that doesn't fire on this play
    }

    [Fact]
    public void Catalog_ListsEveryCompoundingItem_WithItsRarity()
    {
        Assert.Equal(DeskItemRarity.Rare, DeskItemCatalog.Find("column-inches")!.Rarity);
        Assert.Equal(DeskItemRarity.Rare, DeskItemCatalog.Find("hot-type")!.Rarity);
        Assert.Equal(DeskItemRarity.Rare, DeskItemCatalog.Find("interlock")!.Rarity);
        Assert.Equal(DeskItemRarity.Uncommon, DeskItemCatalog.Find("evening-edition")!.Rarity);
        Assert.Equal(DeskItemRarity.Rare, DeskItemCatalog.Find("feature-story")!.Rarity);
        Assert.Equal(DeskItemRarity.Uncommon, DeskItemCatalog.Find("ad-revenue")!.Rarity);
        Assert.Equal(DeskItemRarity.Epic, DeskItemCatalog.Find("subscription-drive")!.Rarity);
        Assert.Equal(DeskItemRarity.Rare, DeskItemCatalog.Find("newsstand")!.Rarity);
        Assert.Equal(DeskItemRarity.Uncommon, DeskItemCatalog.Find("pull-quote")!.Rarity);
        Assert.Equal(DeskItemRarity.Epic, DeskItemCatalog.Find("carbon-copy")!.Rarity);
    }

    [Fact]
    public void EveryItem_IsPure_AndDescribesItself()
    {
        foreach (var item in DeskItemCatalog.All.Where(i => i.Id is "column-inches" or "hot-type" or "interlock" or "evening-edition"
                     or "feature-story" or "ad-revenue" or "subscription-drive" or "newsstand" or "pull-quote" or "carbon-copy"))
        {
            var start = Start(ParallelPlay, new ScoreEnvironment(23, 2, 3) { SubmissionsMade = 2 });
            var once = item.Apply(start);
            var twice = item.Apply(start);
            Assert.Equal((once.Chips, once.Mult, once.Money), (twice.Chips, twice.Mult, twice.Money));
            Assert.False(string.IsNullOrWhiteSpace(item.Description), item.Id);
        }
    }
}
