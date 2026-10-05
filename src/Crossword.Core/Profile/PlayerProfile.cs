using System.Collections.Immutable;
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
    public int RunsWon { get; init; }

    /// <summary>Furthest week reached (1-based; 0 = none yet).</summary>
    public int BestWeekReached { get; init; }

    public long BestPlayScore { get; init; }

    /// <summary>The words of the best-scoring play, joined with " + ".</summary>
    public string? BestPlayWords { get; init; }

    public string? LongestWord { get; init; }
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
        };
    }

    public static PlayerStats RecordRunStart(PlayerStats stats) => stats with { RunsStarted = stats.RunsStarted + 1 };

    /// <param name="weekReached">1-based week the run ended in (or the last week, for a win).</param>
    public static PlayerStats RecordRunEnd(PlayerStats stats, bool won, int weekReached) => stats with
    {
        RunsWon = stats.RunsWon + (won ? 1 : 0),
        BestWeekReached = Math.Max(stats.BestWeekReached, weekReached),
    };
}
