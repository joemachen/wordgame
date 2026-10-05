using System.Text;
using Crossword.Core.Analysis;
using Crossword.Core.Run;

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

    public static string FormatRuns(IReadOnlyList<SimulatedRun> runs, RunConfig config, double skill,
        ShopStrategy strategy = ShopStrategy.Evaluating, SkillModel model = SkillModel.Percentile)
    {
        var sb = new StringBuilder();
        string skillLabel = model == SkillModel.ScoreFraction ? $"{skill:0.00} of best score" : $"{skill:0.00}";
        sb.AppendLine($"Run simulation: {runs.Count} runs at skill {skillLabel} ({(strategy == ShopStrategy.Naive ? "naive" : "evaluating")} shop bot):");
        sb.AppendLine($"  Victory {100.0 * runs.Count(r => r.Victory) / runs.Count:0}%   average rounds cleared {runs.Average(r => r.RoundsCleared):0.0}/{config.TotalRounds}");
        sb.AppendLine($"  Average unspent money at the end ${runs.Average(r => r.FinalMoney):0.0}");
        for (int week = 0; week < config.WeekTargets.Length; week++)
        {
            int reached = runs.Count(r => r.Rounds.Any(x => config.WeekOf(x.RoundIndex) == week));
            sb.AppendLine($"  Reached week {week + 1}: {100.0 * reached / runs.Count,3:0}%");
        }
        AppendSubmissionsToWin(sb, runs, config);
        var used = runs.SelectMany(r => r.StationeryUsed).GroupBy(id => id).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {(double)g.Count() / runs.Count:0.00}").ToList();
        sb.AppendLine($"  Stationery used per run: {(used.Count == 0 ? "none" : string.Join(", ", used))}");
        var deaths = runs.Where(r => !r.Victory).Select(r => r.Rounds[^1]).GroupBy(r => r.Boss ?? r.Kind)
            .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
        sb.Append($"  Lost on: {string.Join(", ", deaths)}");
        return sb.ToString();
    }

    /// <summary>Mean submissions used in won rounds, per week and day kind (the "how long does a round last" feel).</summary>
    private static void AppendSubmissionsToWin(StringBuilder sb, IReadOnlyList<SimulatedRun> runs, RunConfig config)
    {
        var won = runs.SelectMany(r => r.Rounds).Where(r => r.Won).ToList();
        if (won.Count == 0)
            return;
        sb.AppendLine($"  Submissions to win (mean over won rounds; {100.0 * won.Count(r => r.Submissions <= 2) / won.Count:0}% won in 1-2):");
        sb.AppendLine($"    {"",-8}{string.Join("", config.Days.Select(d => $"{d.Name,18}"))}");
        for (int week = 0; week < config.WeekTargets.Length; week++)
        {
            var cells = Enumerable.Range(0, config.Days.Length).Select(day =>
            {
                var rounds = won.Where(r => r.RoundIndex == week * config.Days.Length + day).ToList();
                return rounds.Count == 0 ? $"{"-",18}" : $"{rounds.Average(r => r.Submissions),18:0.00}";
            });
            sb.AppendLine($"    {$"Week {week + 1}",-8}{string.Join("", cells)}");
        }
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
