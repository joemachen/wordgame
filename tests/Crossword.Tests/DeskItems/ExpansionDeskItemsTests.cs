using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

[Trait("Category", "Scoring")]
public class ExpansionDeskItemsTests
{
    // CAT across on an empty board: 1 word, 3 letters, 1 vowel placed, no intersections.
    private static readonly PlayAnalysis SimplePlay = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

    // TO under AT in CAT: words TO, AT, TO — 3 words, 2 intersections, 1 vowel placed.
    private static readonly PlayAnalysis ParallelPlay =
        PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");

    // STAR across: 4 letters.
    private static readonly PlayAnalysis FourLetterPlay = PlayOn(Board.Empty(5), "STAR", 0, 0, Direction.Across, "STAR");

    private static ScoreContext Start(PlayAnalysis play, ScoreEnvironment? env = null) =>
        ScoreContext.Start(play, chips: 10, mult: 2) with { Env = env ?? ScoreEnvironment.Empty };

    private static void AssertNoEffect(IDeskItem item, ScoreContext start) => Assert.Same(start, item.Apply(start));

    [Fact]
    public void MarginNotes_AddsMultPerDiscardLeft()
    {
        Assert.Equal(2m + 3 * 2, new MarginNotes(2).Apply(Start(SimplePlay, new ScoreEnvironment(0, 4, 3))).Mult);
        AssertNoEffect(new MarginNotes(2), Start(SimplePlay, new ScoreEnvironment(0, 4, 0)));
    }

    [Fact]
    public void VowelSound_AddsChipsPerVowelPlaced()
    {
        Assert.Equal(10 + 6, new VowelSound(6).Apply(Start(SimplePlay)).Chips);
    }

    [Fact]
    public void ShortStory_TriggersOnlyForShortLongestWord()
    {
        Assert.Equal(6m, new ShortStory(MaxLength: 3, Mult: 4).Apply(Start(SimplePlay)).Mult);
        AssertNoEffect(new ShortStory(MaxLength: 3, Mult: 4), Start(FourLetterPlay));
    }

    [Fact]
    public void GridLock_AddsChipsPerIntersection()
    {
        Assert.Equal(10 + 30, new GridLock(15).Apply(Start(ParallelPlay)).Chips);
        AssertNoEffect(new GridLock(15), Start(SimplePlay));
    }

    [Fact]
    public void SavingsBond_AddsChipsPerDollarHeld()
    {
        Assert.Equal(10 + 40, new SavingsBond(2).Apply(Start(SimplePlay, new ScoreEnvironment(20, 4, 3))).Chips);
        AssertNoEffect(new SavingsBond(2), Start(SimplePlay));
    }

    [Fact]
    public void DeadlineRush_MultipliesOnlyOnFinalSubmission()
    {
        Assert.Equal(4m, new DeadlineRush(2).Apply(Start(SimplePlay, new ScoreEnvironment(0, 1, 0))).Mult);
        AssertNoEffect(new DeadlineRush(2), Start(SimplePlay, new ScoreEnvironment(0, 2, 0)));
    }

    [Fact]
    public void PremiumStock_TriggersWhenTileOnWordPremium()
    {
        var board = Board.Empty(5) with { Premiums = Board.Empty(5).Premiums.SetItem(2, Premium.DoubleWord) };
        var onPremium = PlayOn(board, "CAT", 0, 0, Direction.Across, "CAT");

        Assert.Equal(6m, new PremiumStock(4).Apply(Start(onPremium)).Mult);
        AssertNoEffect(new PremiumStock(4), Start(SimplePlay));
    }

    [Fact]
    public void Syndication_EarnsMoneyPerIntersection()
    {
        Assert.Equal(2, new Syndication(1).Apply(Start(ParallelPlay)).Money);
        AssertNoEffect(new Syndication(1), Start(SimplePlay));
    }

    [Fact]
    public void EditorInChief_MultipliesForManyWords()
    {
        Assert.Equal(6m, new EditorInChief(MinWords: 3, Factor: 3).Apply(Start(ParallelPlay)).Mult);
        AssertNoEffect(new EditorInChief(MinWords: 3, Factor: 3), Start(SimplePlay));
    }

    [Fact]
    public void WordCount_GrowsPerTilePlaced_AfterThePlay()
    {
        var item = new WordCount(ChipsPerTile: 2);

        AssertNoEffect(item, Start(SimplePlay));
        var grown = (WordCount)((IDeskItem)item).AfterPlay(SimplePlay);
        Assert.Equal(6, grown.Chips);
        Assert.Equal(16, grown.Apply(Start(SimplePlay)).Chips);
    }

    [Fact]
    public void Archive_GrowsOnlyOnLongWords()
    {
        IDeskItem item = new Archive(MinLength: 4, MultPerLongWord: 1);

        var afterShort = item.AfterPlay(SimplePlay);
        var afterLong = item.AfterPlay(FourLetterPlay);

        Assert.Same(item, afterShort);
        Assert.Equal(1m, ((Archive)afterLong).Mult);
        Assert.Equal(3m, afterLong.Apply(Start(SimplePlay)).Mult);
    }

    [Fact]
    public void Pulitzer_GrowsPerBossCleared()
    {
        IDeskItem item = new Pulitzer(Factor: 1.5m, PerBoss: 0.5m);

        Assert.Same(item, item.AfterRoundWon(wasBoss: false));
        Assert.Equal(2m, ((Pulitzer)item.AfterRoundWon(wasBoss: true)).Factor);
        Assert.Equal(3m, item.Apply(Start(SimplePlay)).Mult);
    }

    [Fact]
    public void RunRules_AppliesAfterPlayHooks_AndPassesEnvironment()
    {
        var session = RunRules.NewGame(1, RunConfig.Default, LexiconLoader.Enable);
        var run = session.Run with { Money = 10 };
        run = run.AddDeskItem(new WordCount(ChipsPerTile: 2)).Value.AddDeskItem(new SavingsBond(1)).Value;
        var round = new RoundState(new RoundConfig(TargetScore: 10_000, BoardSize: 5), Board.Empty(5), TileBag.Empty,
            HandOf("CAT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        session = session with { Run = run, Round = round };

        var outcome = RunRules.Submit(session, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"), Words).Value;

        Assert.Contains(outcome.Score.Log, e => e.SourceId == "savings-bond" && e.Description.Contains("+10 chips"));
        Assert.Equal(6, ((WordCount)outcome.Session.Run.DeskItems[0]).Chips);
    }

    [Fact]
    public void RunRules_AppliesAfterRoundWonHook_WithBossFlag()
    {
        var start = RunRules.NewGame(9, RunConfig.Default, LexiconLoader.Enable);
        var run = (start.Run with { RoundIndex = 2 }).AddDeskItem(new Pulitzer(1.5m, 0.5m)).Value; // Sunday
        var round = new RoundState(new RoundConfig(TargetScore: 10, BoardSize: 5), Board.Empty(5), TileBag.Empty,
            HandOf("CAT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        var session = start with { Run = run, Round = round };

        var next = RunRules.Submit(session, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"), Words).Value.Session;

        Assert.Equal(RunPhase.Shop, next.Phase);
        Assert.Equal(2m, ((Pulitzer)next.Run.DeskItems[0]).Factor);
    }

    [Fact]
    public void Shop_RollsRaritiesRoughlyByWeight()
    {
        var counts = new Dictionary<DeskItemRarity, int>();
        var rng = Rng.FromSeed(1);
        for (int i = 0; i < 500; i++)
        {
            (var shop, rng) = ShopRules.Generate(RunState.New(1), RunConfig.Default, rng);
            foreach (var offer in shop.Offers.OfType<DeskItemOffer>())
                counts[offer.Item.Rarity] = counts.GetValueOrDefault(offer.Item.Rarity) + 1;
        }

        Assert.True(counts[DeskItemRarity.Common] > counts[DeskItemRarity.Uncommon]);
        Assert.True(counts[DeskItemRarity.Uncommon] > counts[DeskItemRarity.Rare]);
        Assert.True(counts[DeskItemRarity.Rare] > 0);
    }

    /// <summary>A stand-in item of any rarity, for pricing tests.</summary>
    private sealed record RarityStub(DeskItemRarity Rarity) : IDeskItem
    {
        public string Id => $"stub-{Rarity}";
        public string Name => Id;
        public string Description => "";
        public ScoreContext Apply(ScoreContext context) => context;
    }

    [Theory]
    [InlineData(DeskItemRarity.Common, 4, 2)]
    [InlineData(DeskItemRarity.Uncommon, 6, 3)]
    [InlineData(DeskItemRarity.Rare, 8, 4)]
    [InlineData(DeskItemRarity.Epic, 10, 5)]
    [InlineData(DeskItemRarity.Legendary, 12, 6)]
    public void Shop_PricesEachRarity(DeskItemRarity rarity, int price, int sell)
    {
        var shop = new ShopConfig(CommonPrice: 4, UncommonPrice: 6, RarePrice: 8, EpicPrice: 10, LegendaryPrice: 12);

        Assert.Equal(price, shop.PriceOf(new RarityStub(rarity)));
        Assert.Equal(sell, shop.SellValueOf(new RarityStub(rarity)));
    }

    [Fact]
    public void Shop_RarityWeights_ListEveryTierInOrder()
    {
        var shop = new ShopConfig(CommonWeight: 50, UncommonWeight: 30, RareWeight: 12, EpicWeight: 6, LegendaryWeight: 2);

        Assert.Equal([(DeskItemRarity.Common, 50), (DeskItemRarity.Uncommon, 30), (DeskItemRarity.Rare, 12), (DeskItemRarity.Epic, 6), (DeskItemRarity.Legendary, 2)],
            shop.RarityWeights().ToArray());
    }
}
