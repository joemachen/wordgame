using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Analysis;

[Trait("Category", "Analysis")]
public class ShopBotTests
{
    private static readonly ScoringConfig Scoring = ScoringConfig.Default with
    {
        Tiers =
        [
            new WordTier(2, 2, 1, LevelChips: 3, LevelMult: 1),
            new WordTier(3, 5, 1, LevelChips: 5, LevelMult: 1),
            new WordTier(4, 10, 2, LevelChips: 10, LevelMult: 1),
            new WordTier(7, 40, 3, LevelChips: 25, LevelMult: 1),
        ],
        IntersectionMult = 3,
    };

    private static readonly ShopConfig Shop = new(CommonPrice: 4, UncommonPrice: 6, RarePrice: 8, RerollBaseCost: 5);

    private static readonly RunConfig Config = RunConfig.Default with { Scoring = Scoring, Shop = Shop };

    private static readonly ShopBotConfig Bot = new(
        CandidatePlays: 20, HistoryWindow: 10, MinGain: 0.03, MustBuyGain: 0.25, Reserve: 0, MaxRerolls: 0,
        RerollSlack: 2, RerollWhenFull: false, EarningsHorizon: 0.5, EnhanceGain: 0, EnhancedTileGain: 0, StrikeGain: 0);

    private static readonly Board Start = BoardFromRows(
        ".......",
        ".......",
        ".......",
        "..CAT..",
        ".......",
        ".......",
        ".......");

    // Env: no money held, 3 submissions left, no discards — so Savings Bond, Deadline Rush and Margin Notes never fire.
    private static readonly ScoreEnvironment Env = new(MoneyHeld: 0, SubmissionsLeft: 3, DiscardsLeft: 0);

    private static ImmutableArray<PlayAnalysis> Plays(Func<PlayAnalysis, bool>? filter = null)
    {
        var plays = MoveGenerator.LegalPlays(Start, HandOf("AERSTO"), Words).Where(filter ?? (_ => true)).ToImmutableArray();
        Assert.NotEmpty(plays);
        return plays;
    }

    private static ShopHistory History(ImmutableArray<PlayAnalysis> plays) =>
        ShopHistory.Empty.Add(new DecisionPoint(0, new RoundConfig(TargetScore: 1), Env, plays, plays[0]), window: 10);

    private static GameSession InShop(int money, int roundIndex, IDeskItem[] owned, params ShopOffer?[] offers)
    {
        var run = RunState.New(1) with { Money = money, RoundIndex = roundIndex, DeskItems = [.. owned] };
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(Config, run, RunPhase.Shop, round,
            new ShopState(offers.ToImmutableArray(), Shop.RerollBaseCost, Rng.FromSeed(5)));
    }

    private static string[] Ids(GameSession session) => session.Run.DeskItems.Select(d => d.Id).ToArray();

    [Fact]
    public void BuysTheStrongerItem_WhenItCanAffordOnlyOne()
    {
        // No J/Q/X/Z in the hand, so Rare Ink never fires.
        var session = InShop(4, 0, [], new DeskItemOffer(new RareInk(), 4), new DeskItemOffer(new RedPen(50), 4));

        var after = EvaluatingShopBot.Shop(session, History(Plays()), Bot);

        Assert.Equal(["red-pen"], Ids(after));
        Assert.Equal(0, after.Run.Money);
    }

    [Fact]
    public void InsertsAddedMult_BeforeTimesMult()
    {
        var session = InShop(4, 0, [new Pulitzer(Factor: 2, PerBoss: 0)], new DeskItemOffer(new RedPen(5), 4));

        var after = EvaluatingShopBot.Shop(session, History(Plays()), Bot);

        Assert.Equal(["red-pen", "pulitzer"], Ids(after));
    }

    [Fact]
    public void ReordersOwnedItems_ForAHigherScore()
    {
        var session = InShop(0, 0, [new Pulitzer(Factor: 2, PerBoss: 0), new RedPen(5)]);

        var after = EvaluatingShopBot.Shop(session, History(Plays()), Bot);

        Assert.Equal(["red-pen", "pulitzer"], Ids(after));
    }

    [Fact]
    public void ReplacesADeadItem_WhenSlotsAreFull()
    {
        IDeskItem[] dead = [new RareInk(), new ShortStory(MaxLength: 1), new DeadlineRush(), new SavingsBond(), new MarginNotes()];
        var session = InShop(4, 0, dead, new DeskItemOffer(new RedPen(10), 4));

        var after = EvaluatingShopBot.Shop(session, History(Plays()), Bot);

        Assert.Equal(RunState.MaxDeskSlots, after.Run.DeskItems.Length);
        Assert.Contains("red-pen", Ids(after));
        Assert.Null(after.Shop!.Offers[0]);
    }

    [Fact]
    public void KeepsAStrongBuild_OverAWeakerOffer()
    {
        IDeskItem[] strong = [new RedPen(10), new Thesaurus(20), new VowelSound(20), new WordCount(Chips: 50), new Pulitzer(2, 0)];
        var session = InShop(20, 0, strong, new DeskItemOffer(new RareInk(), 4));

        var after = EvaluatingShopBot.Shop(session, History(Plays()), Bot);

        Assert.Equal(Ids(session), Ids(after));
        Assert.Equal(20, after.Run.Money);
    }

    [Fact]
    public void ValuesScalingItems_ByTheirProjectedGrowth()
    {
        var offer = new DeskItemOffer(new WordCount(ChipsPerTile: 5, Chips: 0), 4);
        var history = History(Plays());

        // Early in the run the empty Word Count is projected to grow; with one round left there is no time to.
        var early = EvaluatingShopBot.Shop(InShop(4, 0, [], offer), history, Bot);
        var late = EvaluatingShopBot.Shop(InShop(4, Config.TotalRounds - 2, [], offer), history, Bot);

        Assert.Equal(["word-count"], Ids(early));
        Assert.Empty(late.Run.DeskItems);
    }

    [Fact]
    public void MoneyEarningItems_PayForThemselves()
    {
        var history = History(Plays(p => p.Intersections.Length > 0));

        var paying = EvaluatingShopBot.Shop(InShop(4, 0, [], new DeskItemOffer(new Syndication(DollarsPerIntersection: 5), 4)), history, Bot);
        var idle = EvaluatingShopBot.Shop(InShop(4, 0, [], new DeskItemOffer(new Syndication(DollarsPerIntersection: 0), 4)), history, Bot);

        Assert.Equal(["syndication"], Ids(paying));
        Assert.Empty(idle.Run.DeskItems);
    }

    [Fact]
    public void KeepsAReserve_ExceptInTheFinalWeek()
    {
        var bot = Bot with { Reserve = 10, MinGain = 0, MustBuyGain = 100 };
        var offer = new DeskItemOffer(new RedPen(1), 4);
        var history = History(Plays());

        var early = EvaluatingShopBot.Shop(InShop(12, 0, [], offer), history, bot);
        var finalWeek = EvaluatingShopBot.Shop(InShop(12, Config.TotalRounds - 3, [], offer), history, bot);

        Assert.Empty(early.Run.DeskItems);
        Assert.Equal(["red-pen"], Ids(finalWeek));
    }

    [Fact]
    public void BuysTheStyleGuide_ForTheTierItActuallyPlays()
    {
        var history = History(Plays(p => p.LongestWord.Length == 3));
        var session = InShop(3, 0, [],
            new StyleGuideOffer(7, "Omnibus", "7+-letter", 25, 1, 3),
            new StyleGuideOffer(3, "Pocket", "3-letter", 5, 1, 3));

        var after = EvaluatingShopBot.Shop(session, history, Bot);

        Assert.Equal(1, after.Run.TierUpgrades.GetValueOrDefault(3));
        Assert.False(after.Run.TierUpgrades.ContainsKey(7));
    }

    [Fact]
    public void History_KeepsOnlyTheMostRecentWindow()
    {
        var plays = Plays();
        var history = Enumerable.Range(0, 5).Aggregate(ShopHistory.Empty,
            (h, i) => h.Add(new DecisionPoint(i, new RoundConfig(TargetScore: 1), Env, plays, plays[0]), window: 3));

        Assert.Equal([2, 3, 4], history.Decisions.Select(d => d.RoundIndex));
    }
}
