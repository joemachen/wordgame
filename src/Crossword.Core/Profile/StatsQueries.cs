using System.Collections.Immutable;
using Crossword.Core.Run;

namespace Crossword.Core.Profile;

public sealed record WordCount(string Word, int Count);

/// <summary>
/// Words of one length (or, when <see cref="OrLonger"/>, that length and longer): how many distinct words, how many
/// uses, the most-used and the least-used (the least-used list never repeats a most-used word).
/// </summary>
public sealed record LengthBucket(int Length, bool OrLonger, int DistinctWords, int TotalUses,
    ImmutableArray<WordCount> MostUsed, ImmutableArray<WordCount> LeastUsed)
{
    public string Label => OrLonger ? $"{Length}+ letters" : $"{Length} letters";
}

public static class StatsQueries
{
    /// <summary>One bucket per length from 2 up to <paramref name="longest"/>, the last one covering longer words too.</summary>
    public static ImmutableArray<LengthBucket> ByLength(PlayerStats stats, int longest = 7, int listSize = 5)
    {
        var buckets = ImmutableArray.CreateBuilder<LengthBucket>();
        for (int length = 2; length <= longest; length++)
        {
            bool orLonger = length == longest;
            var words = stats.Words
                .Where(kv => orLonger ? kv.Key.Length >= length : kv.Key.Length == length)
                .ToList();
            var most = words
                .OrderByDescending(kv => kv.Value.Count)
                .ThenBy(kv => kv.Value.FirstPlayed)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(listSize)
                .Select(kv => new WordCount(kv.Key, kv.Value.Count))
                .ToImmutableArray();
            var mostWords = most.Select(w => w.Word).ToHashSet();
            var least = words
                .Where(kv => !mostWords.Contains(kv.Key))
                .OrderBy(kv => kv.Value.Count)
                .ThenByDescending(kv => kv.Value.FirstPlayed)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(listSize)
                .Select(kv => new WordCount(kv.Key, kv.Value.Count))
                .ToImmutableArray();
            buckets.Add(new LengthBucket(length, orLonger, words.Count, words.Sum(kv => kv.Value.Count), most, least));
        }
        return buckets.ToImmutable();
    }

    /// <summary>The most recently discovered words (first formed), newest first.</summary>
    public static ImmutableArray<string> Newest(PlayerStats stats, int count = 10) =>
        stats.Words
            .OrderByDescending(kv => kv.Value.FirstPlayed)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(count)
            .Select(kv => kv.Key)
            .ToImmutableArray();

    /// <summary>The highest Press Run this player may start: one above the highest won (Proofreader on a new profile).</summary>
    public static int UnlockedPressRun(PlayerStats stats) =>
        Math.Clamp(stats.HighestPressRunWon + 1, PressRuns.Lowest, PressRuns.Highest);
}
