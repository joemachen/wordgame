using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Run;

public class PressRunTests
{
    private static readonly PressRunConfig Press = new(
        DailyBasePay: 1, WeeklyTargetGrowth: 1.5m, SubmissionsDelta: -1, DiscardsDelta: -2,
        DeskItemPriceIncrease: 2, RerollCostIncrease: 3, CensoredLetters: "RS");

    private static readonly RunConfig Base = RunConfig.Default with
    {
        WeekTargets = [100, 200, 300],
        Shop = new ShopConfig(CommonPrice: 4, UncommonPrice: 6, RarePrice: 8, RerollBaseCost: 5),
    };

    [Fact]
    public void Proofreader_IsTheBaseGame() =>
        Assert.Same(Base, PressRuns.Apply(Base, PressRuns.Lowest, Press));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EachLevel_AddsItsRule_OnTopOfEveryLevelBelow(int level)
    {
        var config = PressRuns.Apply(Base, level, Press);

        Assert.Equal(level >= 2 ? 1 : Base.Days[0].BasePay, config.Days[0].BasePay);
        Assert.Equal(Base.Days[1], config.Days[1]); // only the Daily loses its pay
        Assert.Equal(level >= 3 ? new long[] { 100, 300, 680 } : [100, 200, 300], config.WeekTargets); // 300 × 1.5² = 675 → 680
        Assert.Equal(level >= 4 ? -1 : 0, config.SubmissionsDelta);
        Assert.Equal(level >= 5 ? -2 : 0, config.DiscardsDelta);
        Assert.Equal(level >= 6 ? (6, 8, 10, 8) : (4, 6, 8, 5),
            (config.Shop.CommonPrice, config.Shop.UncommonPrice, config.Shop.RarePrice, config.Shop.RerollBaseCost));
        Assert.Equal(level >= 7 ? "RS" : "", config.CensoredLetters);
        Assert.Equal(level >= 8, config.BossExtraModifier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void UnknownLevels_AreRejected(int level)
    {
        Assert.False(PressRuns.IsLevel(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => PressRuns.Apply(Base, level));
    }

    [Fact]
    public void TheLadder_HasEightNamedLevels()
    {
        Assert.Equal(Enumerable.Range(1, 8), PressRuns.All.Select(p => p.Level));
        Assert.Equal("Final Print Run", PressRuns.Get(8).Name);
    }

    [Fact]
    public void Deltas_ApplyAfterTheBoss()
    {
        var config = Base with { SubmissionsDelta = -1, DiscardsDelta = -1 };
        var boss = new TightDeadline(Submissions: 3);

        var daily = config.RoundConfigFor(0, boss);
        var sunday = config.RoundConfigFor(2, boss);

        Assert.Equal((3, 2), (daily.Submissions, daily.Discards));
        Assert.Equal((2, 2), (sunday.Submissions, sunday.Discards)); // Tight Deadline's 3, then −1
    }

    [Fact]
    public void Deltas_LeaveOneSubmission_AndNeverNegativeDiscards()
    {
        var config = (Base with { SubmissionsDelta = -10, DiscardsDelta = -10 }).RoundConfigFor(0);

        Assert.Equal((1, 0), (config.Submissions, config.Discards));
    }

    [Fact]
    public void NewGame_StoresTheLevel_AndPlaysByItsRules()
    {
        var session = RunRules.NewGame(5, RunConfig.Default, LexiconLoader.Enable, pressRun: 5);
        var press = PressRunConfig.Default;

        Assert.Equal(5, session.Run.PressRun);
        Assert.Equal(new RoundConfig(0).Submissions + press.SubmissionsDelta, session.Round.SubmissionsLeft);
        Assert.Equal(new RoundConfig(0).Discards + press.DiscardsDelta, session.Round.DiscardsLeft);
        Assert.Equal(press.DailyBasePay, session.Kind.BasePay);
        Assert.Null(session.Round.Config.CensoredLetter); // Censored Press is level 7
    }

    [Fact]
    public void NewGame_DefaultsToProofreader()
    {
        var session = RunRules.NewGame(5, RunConfig.Default, LexiconLoader.Enable);

        Assert.Equal(PressRuns.Lowest, session.Run.PressRun);
        Assert.Same(RunConfig.Default, session.Config);
    }

    // ---------------------------------------------------------------- Final Print Run (Reprint)

    [Fact]
    public void Reprint_AddsADifferentEarlyOrMidRule_ToTheSameBoss()
    {
        var reprinting = RunConfig.Default with { BossExtraModifier = true };
        var candidates = BossCatalog.PuzzleMasterCandidates.Select(b => b.Id).ToHashSet();
        var extras = new HashSet<string>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var run = RunState.New(seed);
            for (int week = 0; week < 6; week++)
            {
                var reprint = Assert.IsType<Reprint>(RunRules.BossFor(reprinting, run, week));
                Assert.Equal(RunRules.BossFor(RunConfig.Default, run, week), reprint.Main); // the base boss is unchanged
                Assert.Contains(reprint.Extra!.Id, candidates);
                Assert.DoesNotContain(reprint.Extra.Id, reprint.Main!.Rules().Select(r => r.Id));
                Assert.Equal(reprint, RunRules.BossFor(reprinting, run, week)); // seeded: previews match
                extras.Add(reprint.Extra.Id);
            }
        }
        Assert.True(extras.Count > 1);
    }

    [Fact]
    public void Reprint_PreviewMatchesTheSundayRound()
    {
        var session = RunRules.NewGame(9, RunConfig.Default, LexiconLoader.Enable, run => run with { RoundIndex = 2 }, pressRun: 8);

        var boss = Assert.IsType<Reprint>(session.Round.Config.Boss);
        Assert.Equal(session.WeekBoss, boss);
    }

    [Fact]
    public void Reprint_AppliesBothBossesRules()
    {
        var reprint = new Reprint(new TightDeadline(Submissions: 3), new InkSpill(Pairs: 2));

        var round = reprint.Apply(new RoundConfig(TargetScore: 500));

        Assert.Equal(3, round.Submissions);
        Assert.Equal(2, round.BlockedPairs);
        Assert.Same(reprint, round.Boss);
        Assert.Equal("Tight Deadline + Ink Spill", reprint.Name);
    }

    [Fact]
    public void Reprint_ChainsBothBossesScoringChanges()
    {
        var reprint = new Reprint(new VowelDrought(ValuePerVowel: -2), new RedundantCopy());

        var scoring = new RoundConfig(TargetScore: 500, Boss: reprint).EffectiveScoring(ScoringConfig.Default);

        Assert.Equal(-2, scoring.LetterValues['E']);
        Assert.True(scoring.RepeatWordsScoreZero);
    }

    [Fact]
    public void Reprint_ListsThePuzzleMastersPairAsItsRules()
    {
        var reprint = new Reprint(new PuzzleMaster(new InkSpill(), new VowelDrought()), new RedundantCopy());

        Assert.Equal(["ink-spill", "vowel-drought", "redundant-copy"], reprint.Rules().Select(r => r.Id));
        Assert.IsType<Reprint>(BossCatalog.Find("reprint"));
    }
}
