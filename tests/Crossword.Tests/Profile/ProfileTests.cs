using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Profile;

public class ProfileTests
{
    private static readonly Board Empty5 = Board.Empty(5);

    private static PlayerStats Played(params string[] words)
    {
        var stats = PlayerStats.Empty;
        foreach (var word in words)
            stats = StatsRules.RecordPlay(stats, PlayOn(Empty5, word, 0, 0, Direction.Across, word), score: 10);
        return stats;
    }

    private static PlayerStats WithUses(params (string Word, int Count, long First)[] uses) => PlayerStats.Empty with
    {
        Words = uses.ToImmutableDictionary(u => u.Word, u => new WordUse(u.Count, u.First, u.First)),
        PlaysRecorded = uses.Length == 0 ? 0 : uses.Max(u => u.First),
    };

    // ---------------------------------------------------------------- rules

    [Fact]
    public void RecordPlay_CountsUses_AndKeepsTheFirstPlayed()
    {
        var stats = Played("CAT", "TO", "CAT");

        Assert.Equal(new WordUse(2, 1, 3), stats.Words["CAT"]);
        Assert.Equal(new WordUse(1, 2, 2), stats.Words["TO"]);
        Assert.Equal(3, stats.PlaysRecorded);
    }

    [Fact]
    public void RecordPlay_CountsCrossWords_AndTracksBestAndLongest()
    {
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var play = PlayOn(board, "TO", 1, 1, Direction.Across, "TO"); // forms TO, AT (down) and TO (down)

        var stats = StatsRules.RecordPlay(Played("CAT"), play, score: 50);

        Assert.Equal(play.Words.Length, stats.Words.Values.Sum(w => w.Count) - 1);
        Assert.True(stats.Words.ContainsKey("AT"));
        Assert.Equal(50, stats.BestPlayScore);
        Assert.Equal(string.Join(" + ", play.Words.Select(w => w.Text)), stats.BestPlayWords);
        Assert.Equal("CAT", stats.LongestWord); // a 2-letter play doesn't replace the 3-letter record
    }

    [Fact]
    public void RecordRunStartAndEnd_TrackRunsWinsAndFurthestWeek()
    {
        var stats = StatsRules.RecordRunStart(StatsRules.RecordRunStart(PlayerStats.Empty));
        stats = StatsRules.RecordRunEnd(stats, won: false, weekReached: 3, pressRun: 1, Standard);
        stats = StatsRules.RecordRunEnd(stats, won: true, weekReached: 5, pressRun: 1, Standard);
        stats = StatsRules.RecordRunEnd(stats, won: false, weekReached: 2, pressRun: 1, Standard);

        Assert.Equal(2, stats.RunsStarted);
        Assert.Equal(1, stats.RunsWon);
        Assert.Equal(5, stats.BestWeekReached);
    }

    private const string Standard = Decks.StandardId;

    private static ImmutableDictionary<string, int> Won(params (string Deck, int Level)[] levels) =>
        levels.ToImmutableDictionary(l => l.Deck, l => l.Level);

    [Fact]
    public void RecordRunEnd_AWinUnlocksTheNextPressRun()
    {
        Assert.Equal(1, StatsQueries.UnlockedPressRun(PlayerStats.Empty, Standard));

        var stats = StatsRules.RecordRunEnd(PlayerStats.Empty, won: true, weekReached: 5, pressRun: 1, Standard);
        Assert.Equal(1, stats.HighestPressRunWon[Standard]);
        Assert.Equal(2, StatsQueries.UnlockedPressRun(stats, Standard));

        stats = StatsRules.RecordRunEnd(stats, won: false, weekReached: 5, pressRun: 2, Standard);
        Assert.Equal(2, StatsQueries.UnlockedPressRun(stats, Standard)); // a loss unlocks nothing

        stats = StatsRules.RecordRunEnd(stats, won: true, weekReached: 5, pressRun: 3, Standard);
        stats = StatsRules.RecordRunEnd(stats, won: true, weekReached: 5, pressRun: 1, Standard);
        Assert.Equal(3, stats.HighestPressRunWon[Standard]); // winning a lower level never lowers it
    }

    [Fact]
    public void PressRunUnlocks_AreTrackedPerDeck()
    {
        var stats = StatsRules.RecordRunEnd(PlayerStats.Empty, won: true, weekReached: 5, pressRun: 3, Decks.RedactorId);

        Assert.Equal(4, StatsQueries.UnlockedPressRun(stats, Decks.RedactorId));
        Assert.Equal(1, StatsQueries.UnlockedPressRun(stats, Standard)); // a win with another deck doesn't count
        Assert.Equal(1, StatsQueries.UnlockedPressRun(stats, Decks.CopyEditorId));
    }

    [Fact]
    public void UnlockedDecks_OnePerRunWon_InCatalogOrder()
    {
        Assert.Equal(new[] { Standard }, StatsQueries.UnlockedDecks(PlayerStats.Empty).Select(d => d.Id));
        Assert.False(StatsQueries.HasRunChoices(PlayerStats.Empty));

        var stats = StatsRules.RecordRunEnd(PlayerStats.Empty, won: true, weekReached: 5, pressRun: 1, Standard);
        Assert.Equal(new[] { Standard, Decks.CrosswordDraftId }, StatsQueries.UnlockedDecks(stats).Select(d => d.Id));
        Assert.True(StatsQueries.HasRunChoices(stats));
        Assert.Equal(0, StatsQueries.WinsToUnlock(stats, Decks.Get(Decks.CrosswordDraftId)));
        Assert.Equal(2, StatsQueries.WinsToUnlock(stats, Decks.Get(Decks.CopyEditorId)));

        stats = StatsRules.RecordRunEnd(stats, won: false, weekReached: 2, pressRun: 1, Standard);
        Assert.Equal(2, StatsQueries.UnlockedDecks(stats).Length); // a loss unlocks nothing

        Assert.Equal(Decks.All, StatsQueries.UnlockedDecks(PlayerStats.Empty with { RunsWon = 50 }));
    }

    [Fact]
    public void UnlockedDictionaries_TheFirstComesWithTheLexicographersDeck_ThenOnePerRunWon()
    {
        int deckWins = Decks.All.IndexOf(Decks.Get(Decks.LexicographerId));
        var first = Dictionaries.All[0];

        Assert.Empty(StatsQueries.UnlockedDictionaries(PlayerStats.Empty with { RunsWon = deckWins - 1 }));
        Assert.Equal(1, StatsQueries.WinsToUnlock(PlayerStats.Empty with { RunsWon = deckWins - 1 }, first));
        Assert.Equal([first], StatsQueries.UnlockedDictionaries(PlayerStats.Empty with { RunsWon = deckWins }));
        Assert.Equal(0, StatsQueries.WinsToUnlock(PlayerStats.Empty with { RunsWon = deckWins }, first));
        Assert.Equal(Dictionaries.All, StatsQueries.UnlockedDictionaries(PlayerStats.Empty with { RunsWon = 50 }));
    }

    [Fact]
    public void UnlockedPressRun_StopsAtTheTopLevel()
    {
        Assert.Equal(PressRuns.Highest, StatsQueries.UnlockedPressRun(PlayerStats.Empty with { HighestPressRunWon = Won((Standard, PressRuns.Highest)) }, Standard));
    }

    // ---------------------------------------------------------------- queries

    [Fact]
    public void ByLength_BucketsByLength_WithALongerCatchAll()
    {
        var stats = WithUses(("AT", 3, 1), ("TO", 1, 2), ("CAT", 2, 3), ("STARS", 1, 4), ("CATALOG", 1, 5), ("ELEPHANTS", 2, 6));

        var buckets = StatsQueries.ByLength(stats, longest: 7);

        Assert.Equal([2, 3, 4, 5, 6, 7], buckets.Select(b => b.Length));
        Assert.Equal("7+ letters", buckets[^1].Label);
        Assert.Equal((2, 4), (buckets[0].DistinctWords, buckets[0].TotalUses));
        Assert.Equal(0, buckets[2].DistinctWords);
        Assert.Equal(["ELEPHANTS", "CATALOG"], buckets[^1].MostUsed.Select(w => w.Word));
    }

    [Fact]
    public void ByLength_MostAndLeastUsed_AreOrderedAndDoNotOverlap()
    {
        var stats = WithUses(("AA", 5, 1), ("AB", 4, 2), ("AD", 4, 3), ("AE", 3, 4), ("AG", 2, 5), ("AH", 1, 6), ("AI", 1, 7));

        var bucket = StatsQueries.ByLength(stats, longest: 7, listSize: 3)[0];

        Assert.Equal(["AA", "AB", "AD"], bucket.MostUsed.Select(w => w.Word)); // ties: earlier first
        Assert.Equal(["AI", "AH", "AG"], bucket.LeastUsed.Select(w => w.Word)); // ties: most recent first
        Assert.Empty(bucket.MostUsed.Select(w => w.Word).Intersect(bucket.LeastUsed.Select(w => w.Word)));
    }

    [Fact]
    public void Newest_ListsWordsByFirstPlayed_NewestFirst()
    {
        var stats = Played("CAT", "TO", "CAT", "ACE");

        Assert.Equal(["ACE", "TO", "CAT"], StatsQueries.Newest(stats));
        Assert.Equal(["ACE"], StatsQueries.Newest(stats, count: 1));
    }

    // ---------------------------------------------------------------- json

    [Fact]
    public void StatsSerialization_RoundTrip()
    {
        var stats = StatsRules.RecordRunStart(Played("CAT", "TO", "CAT")) with
        {
            TotalIntersections = 7,
            CloseCalls = 2,
            BossesBeaten = new Dictionary<string, int> { ["Ink Spill"] = 2 }.ToImmutableDictionary(),
            FullSpreadRounds = 1,
            HighestPressRunWon = Won((Standard, 3), (Decks.RedactorId, 1)),
        };
        var profile = PlayerProfile.New("Joe") with { Stats = stats };

        var loaded = ProfileJson.Deserialize(ProfileJson.Serialize(profile)).Value;

        Assert.Equal("Joe", loaded.Name);
        Assert.Equal(profile.Stats.Words.OrderBy(kv => kv.Key), loaded.Stats.Words.OrderBy(kv => kv.Key));
        Assert.Equal(profile.Stats.BossesBeaten.OrderBy(kv => kv.Key), loaded.Stats.BossesBeaten.OrderBy(kv => kv.Key));
        Assert.Equal(profile.Stats.HighestPressRunWon.OrderBy(kv => kv.Key), loaded.Stats.HighestPressRunWon.OrderBy(kv => kv.Key));
        Assert.Equal(profile.Stats with
        {
            Words = loaded.Stats.Words, BossesBeaten = loaded.Stats.BossesBeaten, HighestPressRunWon = loaded.Stats.HighestPressRunWon,
        }, loaded.Stats);
    }

    [Fact]
    public void RecordPlay_AddsIntersections()
    {
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var play = PlayOn(board, "TO", 1, 1, Direction.Across, "TO");

        Assert.Equal(play.Intersections.Length, StatsRules.RecordPlay(PlayerStats.Empty, play, 10).TotalIntersections);
        Assert.True(play.Intersections.Length > 0);
    }

    [Fact]
    public void RecordRoundWon_CountsCloseCalls_Bosses_AndFullSpreads()
    {
        RoundState Won(string topRow, int submissionsLeft, Crossword.Core.Run.BossModifier? boss) =>
            new(new RoundConfig(TargetScore: 1, Boss: boss), BoardFromRows(topRow, ".....", ".....", ".....", "....."),
                TileBag.Empty, Hand.Empty, Crossword.Core.Random.Rng.FromSeed(1), Score: 5, SubmissionsLeft: submissionsLeft, DiscardsLeft: 0);

        var stats = StatsRules.RecordRoundWon(PlayerStats.Empty, Won("CAT..", 0, new Crossword.Core.Run.InkSpill()));
        stats = StatsRules.RecordRoundWon(stats, Won("AEIOU", 2, new Crossword.Core.Run.InkSpill()));
        stats = StatsRules.RecordRoundWon(stats, Won("TO...", 1, null));

        Assert.Equal(1, stats.CloseCalls);
        Assert.Equal(2, stats.BossesBeaten[new Crossword.Core.Run.InkSpill().Name]);
        Assert.Equal(1, stats.FullSpreadRounds);
    }

    [Fact]
    public void Json_MissingFields_TakeDefaults()
    {
        var loaded = ProfileJson.Deserialize("""{ "version": 1, "stats": { "runsStarted": 4 } }""").Value;

        Assert.Equal("Player", loaded.Name);
        Assert.Equal(4, loaded.Stats.RunsStarted);
        Assert.Empty(loaded.Stats.Words);
        Assert.Empty(loaded.Stats.HighestPressRunWon);
    }

    [Fact]
    public void Json_ProfilesFromBeforePressRuns_CountTheirWinsAsProofreader()
    {
        var withWins = ProfileJson.Deserialize("""{ "version": 1, "stats": { "runsWon": 2 } }""").Value;
        var saved = ProfileJson.Deserialize("""{ "version": 1, "stats": { "runsWon": 2, "highestPressRunWon": 4 } }""").Value;

        Assert.Equal(Won((Standard, 1)), withWins.Stats.HighestPressRunWon);
        Assert.Equal(Won((Standard, 4)), saved.Stats.HighestPressRunWon);
    }

    [Fact]
    public void Json_ProfilesFromBeforeDecks_CountTheirPressRunForTheStandardDeck()
    {
        var old = ProfileJson.Deserialize("""{ "version": 1, "stats": { "runsWon": 3, "highestPressRunWon": 5 } }""").Value;

        Assert.Equal(6, StatsQueries.UnlockedPressRun(old.Stats, Standard));
        Assert.Equal(1, StatsQueries.UnlockedPressRun(old.Stats, Decks.RedactorId));
        Assert.DoesNotContain("highestPressRunWon", ProfileJson.Serialize(old)); // rewritten per deck
        Assert.Contains("pressRunsWon", ProfileJson.Serialize(old));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 99 }""")]
    [InlineData("null")]
    public void Json_UnreadableOrNewerFiles_Fail(string json)
    {
        Assert.False(ProfileJson.Deserialize(json).IsOk);
    }
}
