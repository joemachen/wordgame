using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Run;

public class RunRulesTests
{
    private static readonly RunConfig Config = RunConfig.Default with
    {
        WeekTargets = [300, 600, 1200],
        Days =
        [
            new RoundKind("Daily", 1m, BasePay: 3, IsBoss: false),
            new RoundKind("Saturday Stumper", 1.5m, BasePay: 4, IsBoss: false),
            new RoundKind("Sunday Edition", 2m, BasePay: 5, IsBoss: true),
        ],
        Economy = new EconomyConfig(StartingMoney: 4),
    };

    private static GameSession NewGame(ulong seed = 1) => RunRules.NewGame(seed, Config, LexiconLoader.Enable);

    /// <summary>A session whose current round has a known hand ("CATSORE") on a plain 5×5 board.</summary>
    private static GameSession WithKnownRound(GameSession session, long target, int submissions = 4)
    {
        var round = new RoundState(new RoundConfig(TargetScore: target, BoardSize: 5, Submissions: submissions),
            Board.Empty(5), new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('E'))).ToImmutableArray()),
            HandOf("CATSORE"), Rng.FromSeed(2), Score: 0, SubmissionsLeft: submissions, DiscardsLeft: 3);
        return session with { Round = round };
    }

    private static Result<SessionOutcome, RoundError> PlayCat(GameSession s) =>
        RunRules.Submit(s, Spell(s.Round.Board, s.Round.Hand, 0, 0, Direction.Across, "CAT"), Words);

    [Fact]
    public void RunConfig_TargetsFollowWeekBaseAndDayMultiplier()
    {
        Assert.Equal(300, Config.TargetFor(0));
        Assert.Equal(450, Config.TargetFor(1));
        Assert.Equal(600, Config.TargetFor(2));
        Assert.Equal(600, Config.TargetFor(3));
        Assert.Equal(9, Config.TotalRounds);
        Assert.Equal(2400, Config.TargetFor(9)); // endless week 4: 1200 × 2
        Assert.Equal(4800, Config.TargetFor(11)); // Sunday: ×2
    }

    [Fact]
    public void NewGame_StartsDailyRound_WithStartingMoney()
    {
        var session = NewGame();

        Assert.Equal(RunPhase.InRound, session.Phase);
        Assert.Equal(4, session.Run.Money);
        Assert.Equal("Daily", session.Kind.Name);
        Assert.Equal(300, session.Round.Config.TargetScore);
        Assert.Null(session.Round.Config.Boss);
    }

    [Fact]
    public void WinningRound_PaysPaycheck_AndOpensShop()
    {
        var session = WithKnownRound(NewGame(), target: 10);

        var next = PlayCat(session).Value.Session;

        Assert.Equal(RunPhase.Shop, next.Phase);
        Assert.NotNull(next.Shop);
        Assert.Equal(new Payout(Base: 3, UnusedSubmissions: 3, Overkill: 0, Interest: 0), next.LastPayout);
        Assert.Equal(4 + 6, next.Run.Money);
    }

    [Fact]
    public void LosingRound_EndsRunInDefeat()
    {
        var session = WithKnownRound(NewGame(), target: 10_000, submissions: 1);

        Assert.Equal(RunPhase.Defeat, PlayCat(session).Value.Session.Phase);
    }

    [Fact]
    public void WinningFinalRound_IsVictory_ThenEndlessOpensShop()
    {
        var start = NewGame();
        var session = WithKnownRound(start with { Run = start.Run with { RoundIndex = Config.TotalRounds - 1 } }, target: 10);

        var won = PlayCat(session).Value.Session;

        Assert.Equal(RunPhase.Victory, won.Phase);
        Assert.Equal(RunPhase.Shop, RunRules.ContinueEndless(won).Value.Phase);
    }

    [Fact]
    public void LeaveShop_StartsNextRound_WithHigherTarget()
    {
        var shop = PlayCat(WithKnownRound(NewGame(), target: 10)).Value.Session;

        var next = RunRules.LeaveShop(shop, LexiconLoader.Enable).Value;

        Assert.Equal(RunPhase.InRound, next.Phase);
        Assert.Equal(1, next.Run.RoundIndex);
        Assert.Equal("Saturday Stumper", next.Kind.Name);
        Assert.Equal(450, next.Round.Config.TargetScore);
        Assert.Equal(7, next.Round.Hand.Count);
    }

    [Fact]
    public void SundayRound_AppliesTheWeeksBoss()
    {
        var start = NewGame(seed: 9);
        var shop = PlayCat(WithKnownRound(start with { Run = start.Run with { RoundIndex = 1 } }, target: 10)).Value.Session;

        var sunday = RunRules.LeaveShop(shop, LexiconLoader.Enable).Value;

        Assert.Equal("Sunday Edition", sunday.Kind.Name);
        Assert.Equal(sunday.WeekBoss, sunday.Round.Config.Boss);
    }

    private static readonly RunConfig TieredConfig = Config with
    {
        BossTiers =
        [
            new BossTier("Early", FirstWeek: 0, [new InkSpill(), new TightMargins()]),
            new BossTier("Late", FirstWeek: 2, [new StrictGrammarian()]),
        ],
    };

    private static IEnumerable<string> BossIds(int week, int seeds = 40) =>
        Enumerable.Range(1, seeds).Select(seed => RunRules.BossFor(TieredConfig, RunState.New((ulong)seed), week).Id).Distinct();

    [Fact]
    public void BossFor_IsDeterministic_AndVariesWithinTheTier()
    {
        Assert.Equal(RunRules.BossFor(TieredConfig, RunState.New(3), 0), RunRules.BossFor(TieredConfig, RunState.New(3), 0));
        Assert.Equal(["ink-spill", "tight-margins"], BossIds(0).Order());
    }

    [Fact]
    public void TargetFor_AppliesTheWeeksBoss_OnlyOnBossRounds()
    {
        var run = RunState.New(1);
        int sunday = 2 * TieredConfig.RoundsPerWeek + 2; // week 3 boss: The Strict Grammarian

        Assert.Equal((long)(TieredConfig.TargetFor(sunday) * new StrictGrammarian().TargetScale),
            RunRules.TargetFor(TieredConfig, run, sunday));
        Assert.Equal(TieredConfig.TargetFor(sunday - 1), RunRules.TargetFor(TieredConfig, run, sunday - 1));
    }

    [Fact]
    public void BossFor_PicksFromTheWeeksTier()
    {
        Assert.Equal(["ink-spill", "tight-margins"], BossIds(1).Order());
        Assert.Equal(["strict-grammarian"], BossIds(2));
    }

    [Fact]
    public void BossFor_EndlessWeeks_DrawFromEveryBoss()
    {
        var ids = BossIds(TieredConfig.WeekTargets.Length, seeds: 60).ToList();

        Assert.True(ids.Count > 2);
        Assert.All(ids, id => Assert.Contains(BossCatalog.All, b => b.Id == id));
    }

    [Fact]
    public void GildedMoney_IsPaidImmediatelyOnSubmit()
    {
        var session = WithKnownRound(NewGame(), target: 10_000);
        var hand = new Hand([new Tile(0, Letter.From('C'), TileEnhancement.Gilded), new Tile(1, Letter.From('A')), new Tile(2, Letter.From('T'))]);
        session = session with { Round = session.Round with { Hand = hand } };

        var next = PlayCat(session).Value.Session;

        Assert.Equal(4 + ScoringGildedMoney, next.Run.Money);
    }

    private static int ScoringGildedMoney => Config.Scoring.GildedMoney;

    [Fact]
    public void ActionsInWrongPhase_AreRejected()
    {
        var shop = PlayCat(WithKnownRound(NewGame(), target: 10)).Value.Session;

        Assert.False(PlayCat(shop).IsOk);
        Assert.False(RunRules.LeaveShop(NewGame(), LexiconLoader.Enable).IsOk);
        Assert.False(RunRules.ContinueEndless(shop).IsOk);
    }

    [Fact]
    public void SameSeed_SameInputs_SameSession()
    {
        static GameSession Play() =>
            RunRules.LeaveShop(PlayCat(WithKnownRound(RunRules.NewGame(77, Config, LexiconLoader.Enable), 10)).Value.Session,
                LexiconLoader.Enable).Value;

        var a = Play();
        var b = Play();

        Assert.Equal(a.Run.Rng, b.Run.Rng);
        Assert.Equal(a.Round.Hand.Tiles.Select(t => t.Id), b.Round.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(a.Round.Board.Premiums, b.Round.Board.Premiums);
    }
}
