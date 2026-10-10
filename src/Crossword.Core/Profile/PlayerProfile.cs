using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Rules;

namespace Crossword.Core.Profile;

/// <summary>How often a word has been formed, and when (by <see cref="PlayerStats.PlaysRecorded"/> sequence) it was first and last formed.</summary>
public sealed record WordUse(int Count, long FirstPlayed, long LastPlayed);

/// <summary>
/// Lifetime stats for one player profile. Nothing here affects play; it is kept across runs in a profile file.
/// Every word a play forms (main and cross words) counts as a use.
/// </summary>
public sealed record PlayerStats
{
    public static PlayerStats Empty { get; } = new();

    public ImmutableDictionary<string, WordUse> Words { get; init; } = ImmutableDictionary<string, WordUse>.Empty;

    /// <summary>Plays recorded so far; also the recency sequence for <see cref="WordUse"/>.</summary>
    public long PlaysRecorded { get; init; }

    public int RunsStarted { get; init; }
    /// <summary>Runs won on a random seed; each one unlocks the next deck or dictionary (<see cref="StatsQueries"/>).</summary>
    public int RunsWon { get; init; }

    /// <summary>Runs won on a seed the player chose: counted, but they unlock nothing.</summary>
    public int SeededRunsWon { get; init; }

    /// <summary>Furthest week reached (1-based; 0 = none yet).</summary>
    public int BestWeekReached { get; init; }

    public long BestPlayScore { get; init; }

    /// <summary>The words of the best-scoring play, joined with " + ".</summary>
    public string? BestPlayWords { get; init; }

    public string? LongestWord { get; init; }

    /// <summary>New tiles that sat in both an Across and a Down word, summed over every play.</summary>
    public long TotalIntersections { get; init; }

    /// <summary>Rounds won on the last submission.</summary>
    public int CloseCalls { get; init; }

    /// <summary>Boss rounds won, by boss name.</summary>
    public ImmutableDictionary<string, int> BossesBeaten { get; init; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>Rounds won whose final board used all five vowels or ten or more distinct letters.</summary>
    public int FullSpreadRounds { get; init; }

    /// <summary>
    /// The highest Press Run won with each deck (by deck id; missing = none yet); the next one up is unlocked for that
    /// deck (<see cref="StatsQueries.UnlockedPressRun"/>).
    /// </summary>
    public ImmutableDictionary<string, int> HighestPressRunWon { get; init; } = ImmutableDictionary<string, int>.Empty;
}

/// <summary>A named player profile. <see cref="Version"/> lets later releases migrate old files.</summary>
public sealed record PlayerProfile(string Name, PlayerStats Stats)
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public static PlayerProfile New(string name) => new(name, PlayerStats.Empty);
}

/// <summary>Pure transitions for <see cref="PlayerStats"/>.</summary>
public static class StatsRules
{
    public static PlayerStats RecordPlay(PlayerStats stats, PlayAnalysis play, long score)
    {
        long sequence = stats.PlaysRecorded + 1;
        var words = stats.Words.ToBuilder();
        foreach (var word in play.Words)
        {
            words[word.Text] = words.TryGetValue(word.Text, out var use)
                ? use with { Count = use.Count + 1, LastPlayed = sequence }
                : new WordUse(1, sequence, sequence);
        }

        string longest = play.LongestWord.Text;
        bool best = score > stats.BestPlayScore;
        return stats with
        {
            Words = words.ToImmutable(),
            PlaysRecorded = sequence,
            BestPlayScore = best ? score : stats.BestPlayScore,
            BestPlayWords = best ? string.Join(" + ", play.Words.Select(w => w.Text)) : stats.BestPlayWords,
            LongestWord = stats.LongestWord is null || longest.Length > stats.LongestWord.Length ? longest : stats.LongestWord,
            TotalIntersections = stats.TotalIntersections + play.Intersections.Length,
        };
    }

    /// <summary>
    /// Records a won round: a close call when no submissions were left, the boss beaten, and a full spread when the
    /// final board shows all five vowels or ten or more distinct letters (wild tiles count as the letter they play).
    /// </summary>
    public static PlayerStats RecordRoundWon(PlayerStats stats, RoundState round)
    {
        var letters = round.Board.Cells.OfType<Tile>().Select(t => t.Letter.Char).ToHashSet();
        bool fullSpread = "AEIOU".All(letters.Contains) || letters.Count >= 10;
        return stats with
        {
            CloseCalls = stats.CloseCalls + (round.SubmissionsLeft == 0 ? 1 : 0),
            BossesBeaten = round.Config.Boss is { } boss
                ? stats.BossesBeaten.SetItem(boss.Name, stats.BossesBeaten.GetValueOrDefault(boss.Name) + 1)
                : stats.BossesBeaten,
            FullSpreadRounds = stats.FullSpreadRounds + (fullSpread ? 1 : 0),
        };
    }

    public static PlayerStats RecordRunStart(PlayerStats stats) => stats with { RunsStarted = stats.RunsStarted + 1 };

    /// <param name="weekReached">1-based week the run ended in (or the last week, for a win).</param>
    /// <param name="pressRun">The run's Press Run; a win there unlocks the next one for <paramref name="deck"/>.</param>
    /// <param name="deck">The run's deck id; any win also unlocks the next deck (<see cref="StatsQueries.UnlockedDecks"/>).</param>
    /// <param name="seeded">The player chose the seed: a win counts in <see cref="PlayerStats.SeededRunsWon"/> and
    /// unlocks nothing.</param>
    public static PlayerStats RecordRunEnd(PlayerStats stats, bool won, int weekReached, int pressRun, string deck, bool seeded = false) => stats with
    {
        RunsWon = stats.RunsWon + (won && !seeded ? 1 : 0),
        SeededRunsWon = stats.SeededRunsWon + (won && seeded ? 1 : 0),
        BestWeekReached = Math.Max(stats.BestWeekReached, weekReached),
        HighestPressRunWon = won && !seeded
            ? stats.HighestPressRunWon.SetItem(deck, Math.Max(stats.HighestPressRunWon.GetValueOrDefault(deck), pressRun))
            : stats.HighestPressRunWon,
    };
}
