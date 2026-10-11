using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

/// <summary>The newsroom batch (2026-10-10). Every number is pinned here, not read from the items' defaults.</summary>
[Trait("Category", "Scoring")]
public class NewsroomDeskItemsTests
{
    // CAT across on an empty board at (0,0): 1 word, 3 letters, on the edge.
    private static readonly PlayAnalysis SimplePlay = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

    // CAT across at (1,1): the same play, nowhere near an edge.
    private static readonly PlayAnalysis InnerPlay = PlayOn(Board.Empty(5), "CAT", 1, 1, Direction.Across, "CAT");

    // TO under AT in CAT: words TO, AT, TO — 3 words, reuses the old A and T; 4 words on the board afterwards.
    private static readonly PlayAnalysis ParallelPlay =
        PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");

    // STAR across: 4 letters.
    private static readonly PlayAnalysis FourLetterPlay = PlayOn(Board.Empty(5), "STAR", 0, 0, Direction.Across, "STAR");

    private static ScoreContext Start(PlayAnalysis play, ScoreEnvironment? env = null) =>
        ScoreContext.Start(play, chips: 10, mult: 2) with { Env = env ?? ScoreEnvironment.Empty };

    private static void AssertNoEffect(IDeskItem item, ScoreContext start) => Assert.Same(start, item.Apply(start));

    private static PlayAnalysis Placing(params Tile[] tiles) =>
        new(tiles.Select((t, i) => new PlacedTile(new Position(0, i), t)).ToImmutableArray(), [], Board.Empty(5));

    [Fact]
    public void Classifieds_AddsChipsPerShortWord()
    {
        Assert.Equal(10 + 8, new Classifieds(ChipsPerWord: 8, MaxLength: 3).Apply(Start(SimplePlay)).Chips);
        Assert.Equal(10 + 24, new Classifieds(ChipsPerWord: 8, MaxLength: 3).Apply(Start(ParallelPlay)).Chips);
        AssertNoEffect(new Classifieds(ChipsPerWord: 8, MaxLength: 3), Start(FourLetterPlay));
    }

    [Fact]
    public void Typesetter_AddsChipsPerPairOfIdenticalLettersPlaced_IgnoringWilds()
    {
        var a = Letter.From('A');
        var twoPairs = Placing(new Tile(1, a), new Tile(2, a), new Tile(3, Letter.From('T')), new Tile(4, Letter.From('T')), new Tile(5, Letter.From('T')));
        var wildPair = Placing(new Tile(1, a), Tile.Wild(2).As(a));

        Assert.Equal(10 + 24, new Typesetter(12).Apply(Start(twoPairs)).Chips);
        AssertNoEffect(new Typesetter(12), Start(wildPair));
        AssertNoEffect(new Typesetter(12), Start(SimplePlay));
    }

    [Fact]
    public void Byline_AddsMultForATileOnTheEdge()
    {
        Assert.Equal(2m + 3, new Byline(3).Apply(Start(SimplePlay)).Mult);
        AssertNoEffect(new Byline(3), Start(InnerPlay));
    }

    [Fact]
    public void OpEd_AddsMultForASingleWordPlay()
    {
        Assert.Equal(2m + 5, new OpEd(5).Apply(Start(SimplePlay)).Mult);
        AssertNoEffect(new OpEd(5), Start(ParallelPlay));
    }

    [Fact]
    public void LateEdition_AddsMultPerSubmissionAlreadyMade()
    {
        Assert.Equal(2m + 4, new LateEdition(2).Apply(Start(SimplePlay, new ScoreEnvironment(0, 2, 3) { SubmissionsMade = 2 })).Mult);
        AssertNoEffect(new LateEdition(2), Start(SimplePlay, new ScoreEnvironment(0, 4, 3) { SubmissionsMade = 0 }));
    }

    [Fact]
    public void Morgue_AddsChipsPerWordOnTheBoardAfterThePlay()
    {
        Assert.Equal(10 + 4, new Morgue(4).Apply(Start(SimplePlay)).Chips);
        Assert.Equal(10 + 16, new Morgue(4).Apply(Start(ParallelPlay)).Chips); // CAT, TO, AT, TO
    }

    [Fact]
    public void Headline_AddsChipsPerLetterOfEveryWordFormed()
    {
        Assert.Equal(10 + 18, new Headline(6).Apply(Start(SimplePlay)).Chips);
        Assert.Equal(10 + 36, new Headline(6).Apply(Start(ParallelPlay)).Chips); // TO + AT + TO
    }

    [Fact]
    public void LettersToTheEditor_GrowsWithDistinctLettersPlaced_AndAddsMultForThem()
    {
        var fresh = new LettersToTheEditor(MultPerLetter: 0.5m);
        AssertNoEffect(fresh, Start(SimplePlay));

        var afterCat = (LettersToTheEditor)fresh.AfterPlay(SimplePlay);
        Assert.Equal("ACT", afterCat.Letters);
        Assert.Equal(2m + 1.5m, afterCat.Apply(Start(SimplePlay)).Mult);
        Assert.Same(afterCat, afterCat.AfterPlay(SimplePlay)); // nothing new
        Assert.Equal("ACOT", ((LettersToTheEditor)afterCat.AfterPlay(ParallelPlay)).Letters);
        Assert.Equal("A", ((LettersToTheEditor)fresh.AfterPlay(Placing(new Tile(1, Letter.From('A')), Tile.Wild(2).As(Letter.From('Z'))))).Letters);
    }

    [Fact]
    public void FrontPage_CountsEveryWordsLetterChipsAgain()
    {
        Assert.Equal(10 + 9, new FrontPage().Apply(Start(ParallelPlay) with { WordChips = [2, 2, 5] }).Chips);
        AssertNoEffect(new FrontPage(), Start(SimplePlay));
    }

    [Fact]
    public void CrosswordEditor_AddsMultPerOldTileAFormedWordRunsThrough()
    {
        Assert.Equal(2m + 2, new CrosswordEditor(1).Apply(Start(ParallelPlay)).Mult); // the old A and T, each once
        AssertNoEffect(new CrosswordEditor(1), Start(SimplePlay));
    }

    [Fact]
    public void ExtraExtra_AddsASubmissionEveryRound_AfterTheBoss()
    {
        Assert.Equal(5, new ExtraExtra(1).ModifyRound(new RoundConfig(TargetScore: 500)).Submissions);
        Assert.Equal(4, new ExtraExtra(1).ModifyRound(new TightDeadline(3).Apply(new RoundConfig(TargetScore: 500))).Submissions);

        var session = RunRules.NewGame(3, RunConfig.Default, LexiconLoader.Enable);
        var shop = session with { Run = session.Run.AddDeskItem(new ExtraExtra(1)).Value, Phase = RunPhase.Shop };
        var next = RunRules.LeaveShop(shop, LexiconLoader.Enable).Value;
        Assert.Equal(RunConfig.Default.RoundConfigFor(next.Run.RoundIndex).Submissions + 1, next.Round.SubmissionsLeft);
        Assert.Equal(RunRules.TargetFor(next.Config, next.Run, next.Run.RoundIndex), next.Round.Config.TargetScore);
    }

    [Fact]
    public void SecondPrinting_MultipliesChipsAndMult_SoSlotOrderMatters()
    {
        var doubled = new SecondPrinting(2).Apply(Start(SimplePlay));
        Assert.Equal((20L, 4m), (doubled.Chips, doubled.Mult));

        var redPenFirst = EffectPipeline.Apply([new RedPen(2), new SecondPrinting(2)], Start(SimplePlay));
        var printingFirst = EffectPipeline.Apply([new SecondPrinting(2), new RedPen(2)], Start(SimplePlay));
        Assert.Equal(8m, redPenFirst.Mult);
        Assert.Equal(6m, printingFirst.Mult);
    }

    [Fact]
    public void Catalog_ListsEveryNewsroomItem_WithItsRarity()
    {
        Assert.Equal(DeskItemRarity.Epic, DeskItemCatalog.Find("front-page")!.Rarity);
        Assert.Equal(DeskItemRarity.Epic, DeskItemCatalog.Find("crossword-editor")!.Rarity);
        Assert.Equal(DeskItemRarity.Legendary, DeskItemCatalog.Find("extra-extra")!.Rarity);
        Assert.Equal(DeskItemRarity.Legendary, DeskItemCatalog.Find("second-printing")!.Rarity);
        Assert.Equal(35, DeskItemCatalog.All.Length);
        Assert.Equal(DeskItemCatalog.All.Length, DeskItemCatalog.All.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Shop_RollsEveryRarity_RarestLast()
    {
        var counts = new Dictionary<DeskItemRarity, int>();
        var rng = Crossword.Core.Random.Rng.FromSeed(7);
        for (int i = 0; i < 1500; i++)
        {
            (var shop, rng) = ShopRules.Generate(RunState.New(1), RunConfig.Default, rng);
            foreach (var offer in shop.Offers.OfType<DeskItemOffer>())
                counts[offer.Item.Rarity] = counts.GetValueOrDefault(offer.Item.Rarity) + 1;
        }

        Assert.True(counts[DeskItemRarity.Common] > counts[DeskItemRarity.Uncommon]);
        Assert.True(counts[DeskItemRarity.Uncommon] > counts[DeskItemRarity.Rare]);
        Assert.True(counts[DeskItemRarity.Rare] > counts[DeskItemRarity.Epic]);
        Assert.True(counts[DeskItemRarity.Epic] > counts[DeskItemRarity.Legendary]);
        Assert.True(counts[DeskItemRarity.Legendary] > 0);
    }
}
