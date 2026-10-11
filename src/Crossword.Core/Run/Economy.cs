using Crossword.Core.Domain;

namespace Crossword.Core.Run;

/// <summary>Itemised end-of-round earnings (the Paycheck).</summary>
public sealed record Payout(int Base, int UnusedSubmissions, int Overkill, int Interest)
{
    /// <summary>Paid for discards left over (<see cref="EconomyConfig.PerUnusedDiscard"/>; 0 unless turned on).</summary>
    public int UnusedDiscards { get; init; }

    /// <summary>Top-up to <see cref="EconomyConfig.MinPaycheck"/> when the other parts fall short of it.</summary>
    public int FloorTopUp { get; init; }

    public int Total => Base + UnusedSubmissions + UnusedDiscards + Overkill + Interest + FloorTopUp;
}

public static class Economy
{
    /// <summary>
    /// Paycheck for a won round. Interest is computed on money held before the paycheck is added.
    /// Overkill: +1 per full <see cref="EconomyConfig.OverkillStep"/> of the target exceeded, capped.
    /// </summary>
    public static Payout Calculate(EconomyConfig config, RoundKind kind, RoundState round, int moneyBeforePayout)
    {
        long target = round.Config.TargetScore;
        int overkill = 0;
        if (target > 0 && round.Score > target)
        {
            decimal ratio = (decimal)(round.Score - target) / target;
            overkill = (int)Math.Min(config.OverkillCap, decimal.Floor(ratio / config.OverkillStep));
        }

        int interest = Math.Min(config.InterestCap, Math.Max(0, moneyBeforePayout) / config.InterestPer);

        var payout = new Payout(
            Base: kind.BasePay,
            UnusedSubmissions: Math.Max(0, round.SubmissionsLeft) * config.PerUnusedSubmission,
            Overkill: overkill,
            Interest: interest)
        {
            UnusedDiscards = Math.Max(0, round.DiscardsLeft) * config.PerUnusedDiscard,
        };
        return payout.Total < config.MinPaycheck ? payout with { FloorTopUp = config.MinPaycheck - payout.Total } : payout;
    }

    /// <summary>One line for the paycheck screen saying where money comes from, built from the config's numbers.</summary>
    public static string HowToEarnMore(EconomyConfig config)
    {
        var parts = new List<string>();
        if (config.PerUnusedSubmission > 0)
            parts.Add($"+${config.PerUnusedSubmission} per unused submission");
        if (config.PerUnusedDiscard > 0)
            parts.Add($"+${config.PerUnusedDiscard} per unused discard");
        if (config.OverkillCap > 0)
            parts.Add($"+$1 per {config.OverkillStep:P0} over the deadline (max ${config.OverkillCap})");
        if (config.InterestCap > 0)
            parts.Add($"+$1 interest per ${config.InterestPer} held (max ${config.InterestCap})");
        if (config.MinPaycheck > 0)
            parts.Add($"every paycheck is at least ${config.MinPaycheck}");
        return "Earn more: " + string.Join("  ·  ", parts);
    }
}
