using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Run;

[Trait("Category", "Scoring")]
public class StyleGuideTests
{
    private static readonly ScoringConfig Config = ScoringConfig.Default with
    {
        Tiers = [new WordTier(2, 2, 1, 3, 1), new WordTier(3, 5, 1, 5, 1), new WordTier(4, 10, 2, 10, 1)],
    };

    [Fact]
    public void WithUpgrades_AddsLevelBonusPerUpgrade_OnlyToThatTier()
    {
        var upgraded = Config.WithUpgrades(new Dictionary<int, int> { [3] = 2 });

        Assert.Equal(new WordTier(3, 15, 3, 5, 1), upgraded.TierFor(3));
        Assert.Equal(Config.TierFor(2), upgraded.TierFor(2));
        Assert.Equal(Config.TierFor(4), upgraded.TierFor(4));
    }

    [Fact]
    public void WithUpgrades_Empty_ReturnsSameConfig()
    {
        Assert.Same(Config, Config.WithUpgrades(ImmutableDictionary<int, int>.Empty));
    }

    [Fact]
    public void UpgradeTier_AccumulatesOnRunState()
    {
        var run = RunState.New(1).UpgradeTier(5).UpgradeTier(5).UpgradeTier(3);

        Assert.Equal(2, run.TierUpgrades[5]);
        Assert.Equal(1, run.TierUpgrades[3]);
    }

    [Fact]
    public void Submit_ScoresWithUpgradedTier()
    {
        var config = RunConfig.Default with { Scoring = Config };
        var session = RunRules.NewGame(1, config, LexiconLoader.Enable);
        var round = new RoundState(new RoundConfig(TargetScore: 10_000, BoardSize: 5), Board.Empty(5), TileBag.Empty,
            HandOf("CAT"), Rng.FromSeed(1), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        session = session with { Round = round, Run = session.Run.UpgradeTier(3) };

        var outcome = RunRules.Submit(session, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"), Words).Value;

        Assert.Equal((10 + 5) * 2, outcome.Score.Total); // tier 3 upgraded once: 5+5 chips, 1+1 mult; letters 5
    }
}
