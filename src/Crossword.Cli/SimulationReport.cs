using System.Text;
using Crossword.Core.Analysis;

namespace Crossword.Cli;

/// <summary>Summarises greedy-play simulations for balance tuning.</summary>
public static class SimulationReport
{
    public static string Format(IReadOnlyList<SimulatedRound> rounds, long target)
    {
        var plays = rounds.SelectMany(r => r.Plays).ToList();
        var totals = rounds.Select(r => r.FinalScore).Order().ToList();
        var sb = new StringBuilder();

        sb.AppendLine($"Greedy simulation over {rounds.Count} rounds (upper bound on skilled play):");
        int maxSubmissions = rounds.Max(r => r.Plays.Length);
        for (int i = 1; i <= maxSubmissions; i++)
        {
            var scores = plays.Where(p => p.Submission == i).Select(p => p.Score).Order().ToList();
            sb.AppendLine($"  Submission {i}: mean {scores.Average(),6:0}   median {Percentile(scores, 50),5}   p90 {Percentile(scores, 90),5}");
        }

        sb.AppendLine($"  Round total:  mean {totals.Average(),6:0}   p10 {Percentile(totals, 10)}   median {Percentile(totals, 50)}   p90 {Percentile(totals, 90)}");
        sb.AppendLine($"  Plays: longest word {plays.Average(p => p.LongestWord):0.0} letters, " +
                      $"{plays.Average(p => p.TilesUsed):0.0} tiles, {plays.Average(p => p.WordsFormed):0.0} words, " +
                      $"{plays.Average(p => p.Intersections):0.00} intersections " +
                      $"({100.0 * plays.Count(p => p.Intersections > 0) / plays.Count:0}% of plays intersect)");
        sb.AppendLine($"  Discards used {rounds.Average(r => r.DiscardsUsed):0.00}/round, deadlocks {rounds.Count(r => r.Deadlocked)}");

        var submissionsToWin = rounds.Select(r => SubmissionsToReach(r, target)).ToList();
        int wins = submissionsToWin.Count(s => s is not null);
        sb.Append($"  vs target {target}: greedy wins {100.0 * wins / rounds.Count:0}%");
        if (wins > 0)
            sb.Append($", needing {submissionsToWin.Where(s => s is not null).Average(s => s!.Value):0.0} submissions on average");
        return sb.ToString();
    }

    private static int? SubmissionsToReach(SimulatedRound round, long target)
    {
        long running = 0;
        foreach (var play in round.Plays)
        {
            running += play.Score;
            if (running >= target)
                return play.Submission;
        }
        return null;
    }

    private static long Percentile(IReadOnlyList<long> sorted, int percentile) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, sorted.Count * percentile / 100)];
}
