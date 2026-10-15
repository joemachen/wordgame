using System.Collections.Immutable;
using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Crossword.Core.Run;
using Crossword.Core.Save;

namespace Crossword.Tests.Run;

public class CirculationTests
{
    private static readonly RunConfig Config = RunConfig.Default with
    {
        WeekTargets = [100, 200, 400, 800, 1600],
        Shop = RunConfig.Default.Shop with { RareWeight = 10, EpicWeight = 4, LegendaryWeight = 2 },
    };

    [Fact]
    public void BaseLevel_ChangesNothing()
    {
        Assert.Same(Config, Circulations.Apply(Config, Circulations.Lowest));
        Assert.Same(Config, RunRules.ConfigFor(Config, Decks.StandardId, 1, [], Circulations.Lowest));
    }

    [Fact]
    public void Levels_AreOrdered_AndEachRampsFromOneToItsPrintRun()
    {
        Assert.Equal(Enumerable.Range(1, Circulations.All.Length), Circulations.All.Select(c => c.Level));
        for (int i = 0; i < Circulations.All.Length; i++)
        {
            var c = Circulations.All[i];
            Assert.Equal(1, c.WeekScales[0]);
            Assert.Equal(c.PrintRun, c.WeekScales[^1]);
            Assert.True(c.WeekScales.Zip(c.WeekScales.Skip(1)).All(p => p.First <= p.Second));
            if (i > 0)
                Assert.True(c.PrintRun > Circulations.All[i - 1].PrintRun);
        }
    }

    [Fact]
    public void Apply_RampsDeadlines_RaisesPay_AndBoostsRarity()
    {
        var level = Circulations.All[2];
        var applied = Circulations.Apply(Config, level.Level);

        Assert.Equal(Config.TargetFor(0), applied.TargetFor(0)); // Week 1 plays like the base game
        Assert.Equal((long)(Config.TargetFor(14) * level.PrintRun), applied.TargetFor(14));
        Assert.Equal(Config.Days.Select(d => d.BasePay + level.BasePayBonus), applied.Days.Select(d => d.BasePay));
        Assert.Equal((int)decimal.Round(10 * level.RarityBoost, MidpointRounding.AwayFromZero), applied.Shop.RareWeight);
        Assert.Equal(Config.Shop.CommonWeight, applied.Shop.CommonWeight);
    }

    [Fact]
    public void NewGame_StoresTheLevel_AndASaveReappliesIt()
    {
        var session = RunRules.NewGame(5, Config, LexiconLoader.Enable, circulation: 3);
        Assert.Equal(3, session.Run.Circulation);
        Assert.Equal(Circulations.Apply(Config, 3).TargetFor(14), session.Config.TargetFor(14));

        var loaded = RunSaveJson.Deserialize(RunSaveJson.Serialize(session), Config).Value.Session;
        Assert.Equal(3, loaded.Run.Circulation);
        Assert.Equal(session.Config.WeekScales, loaded.Config.WeekScales);

        string json = RunSaveJson.Serialize(session);
        Assert.False(RunSaveJson.Deserialize(json.Replace("\"circulation\": 3", "\"circulation\": 99"), Config).IsOk);
    }

    [Fact]
    public void Circulation_StacksWithPressRunsAndDecks()
    {
        var config = RunRules.ConfigFor(Config, Decks.CrosswordDraftId, 3, [], 2);
        var noDial = RunRules.ConfigFor(Config, Decks.CrosswordDraftId, 3, []);

        Assert.Equal(noDial.TargetFor(0), config.TargetFor(0));
        Assert.True(config.TargetFor(14) > noDial.TargetFor(14));
    }

    [Fact]
    public void Winning_UnlocksTheNextLevel_ButNotOnASeededRun()
    {
        Assert.Equal(1, StatsQueries.UnlockedCirculation(PlayerStats.Empty));

        var stats = StatsRules.RecordRunEnd(PlayerStats.Empty, won: true, weekReached: 5, pressRun: 1, Decks.StandardId, circulation: 1);
        Assert.Equal(2, StatsQueries.UnlockedCirculation(stats));
        stats = StatsRules.RecordRunEnd(stats, won: false, weekReached: 5, pressRun: 1, Decks.StandardId, circulation: 2);
        Assert.Equal(2, StatsQueries.UnlockedCirculation(stats));
        stats = StatsRules.RecordRunEnd(stats, won: true, weekReached: 5, pressRun: 4, Decks.RedactorId, seeded: true, circulation: 2);
        Assert.Equal(2, StatsQueries.UnlockedCirculation(stats));
        stats = StatsRules.RecordRunEnd(stats, won: true, weekReached: 5, pressRun: 4, Decks.RedactorId, circulation: Circulations.Highest);
        Assert.Equal(Circulations.Highest, StatsQueries.UnlockedCirculation(stats));
    }

    [Fact]
    public void Profiles_KeepTheLevel_AndOldProfilesWithWinsUnlockTheSecond()
    {
        var stats = PlayerStats.Empty with { RunsWon = 2, HighestCirculationWon = 3 };
        var round = ProfileJson.Deserialize(ProfileJson.Serialize(new PlayerProfile("A", stats))).Value.Stats;
        Assert.Equal(3, round.HighestCirculationWon);

        string old = System.Text.RegularExpressions.Regex.Replace(
            ProfileJson.Serialize(new PlayerProfile("B", PlayerStats.Empty with { RunsWon = 1 })), @",\s*""highestCirculationWon"": 0", "");
        Assert.DoesNotContain("highestCirculationWon", old);
        Assert.Equal(2, StatsQueries.UnlockedCirculation(ProfileJson.Deserialize(old).Value.Stats));
    }
}
