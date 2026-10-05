using Crossword.Core.Domain;

namespace Crossword.Core.Run;

/// <summary>Itemised end-of-round earnings (the Paycheck).</summary>
public sealed record Payout(int Base, int UnusedSubmissions, int Overkill, int Interest)
{
    public int Total => Base + UnusedSubmissions + Overkill + Interest;
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

        return new Payout(
            Base: kind.BasePay,
            UnusedSubmissions: Math.Max(0, round.SubmissionsLeft) * config.PerUnusedSubmission,
            Overkill: overkill,
            Interest: interest);
    }
}
