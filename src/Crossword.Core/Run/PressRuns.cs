using System.Collections.Immutable;

namespace Crossword.Core.Run;

/// <summary>One difficulty level of the Press Run ladder. <see cref="Color"/> is a hex color for the UI.</summary>
public sealed record PressRun(int Level, string Name, string Color, string Adds);

/// <summary>
/// The numbers behind each Press Run's rule, tuned with <c>runsim … press=N</c>. Each level adds one rule on top of
/// every level below it. First measured (2026-10-06, strong player = ScoreFraction 0.9, 150 runs, each rule alone vs
/// 55% wins): −1 submission on every round −34 pts, censoring one of D/L/N/R/S/T −26, targets ×1.1 per week −14 — a
/// stacked ladder at 0% from level 5. Softened (user's choice: an even ladder, ~55% → ~10% for the strong player) to
/// −1 submission on Sundays only, rarer consonants, ×1.05 per week, Dailies paying $1 and Heavy Printing raising only
/// rerolls: 0.9 wins 55 / 44.5 / 36.5 / 23 / 18.5 / 15.5 / 13.5 / 7.5% at levels 1–8 (200 runs each).
/// </summary>
public sealed record PressRunConfig(
    int DailyBasePay = 1,
    decimal WeeklyTargetGrowth = 1.05m,
    int BossSubmissionsDelta = -1,
    int DiscardsDelta = -1,
    int DeskItemPriceIncrease = 0,
    int RerollCostIncrease = 1,
    string CensoredLetters = "BCFGHMPWY")
{
    public static PressRunConfig Default { get; } = new();
}

/// <summary>
/// Difficulty levels ("stakes"): level N is unlocked by winning a run at level N − 1 (<see cref="Profile.StatsQueries"/>).
/// A run's level lives in <see cref="Domain.RunState.PressRun"/>; its rules are a <see cref="RunConfig"/> transform
/// (<see cref="Apply"/>), applied when the run starts and again when it's loaded.
/// </summary>
public static class PressRuns
{
    public const int Lowest = 1;
    public const int Highest = 8;

    public static ImmutableArray<PressRun> All { get; } = Describe(PressRunConfig.Default);

    public static PressRun Get(int level) =>
        IsLevel(level) ? All[level - 1] : throw new ArgumentOutOfRangeException(nameof(level), level, "No such Press Run.");

    public static bool IsLevel(int level) => level is >= Lowest and <= Highest;

    /// <summary>The rules of <paramref name="level"/> applied to <paramref name="config"/> (level 1 = unchanged).</summary>
    public static RunConfig Apply(RunConfig config, int level, PressRunConfig? press = null)
    {
        if (!IsLevel(level))
            throw new ArgumentOutOfRangeException(nameof(level), level, "No such Press Run.");
        var p = press ?? PressRunConfig.Default;

        if (level >= 2)
            config = config with { Days = config.Days.Select((day, i) => i == 0 ? day with { BasePay = p.DailyBasePay } : day).ToImmutableArray() };
        if (level >= 3)
            config = config with { WeekTargets = config.WeekTargets.Select((target, week) => Grow(target, p.WeeklyTargetGrowth, week)).ToImmutableArray() };
        if (level >= 4)
            config = config with { BossSubmissionsDelta = config.BossSubmissionsDelta + p.BossSubmissionsDelta };
        if (level >= 5)
            config = config with { DiscardsDelta = config.DiscardsDelta + p.DiscardsDelta };
        if (level >= 6)
            config = config with
            {
                Shop = config.Shop with
                {
                    CommonPrice = config.Shop.CommonPrice + p.DeskItemPriceIncrease,
                    UncommonPrice = config.Shop.UncommonPrice + p.DeskItemPriceIncrease,
                    RarePrice = config.Shop.RarePrice + p.DeskItemPriceIncrease,
                    RerollBaseCost = config.Shop.RerollBaseCost + p.RerollCostIncrease,
                },
            };
        if (level >= 7)
            config = config with { CensoredLetters = p.CensoredLetters };
        if (level >= 8)
            config = config with { BossExtraModifier = true };
        return config;
    }

    /// <summary>Week <paramref name="week"/> (0-based) of the target table grown <paramref name="growth"/>× per week, rounded to 10.</summary>
    private static long Grow(long target, decimal growth, int week) =>
        (long)Math.Round(target * (decimal)Math.Pow((double)growth, week) / 10m, MidpointRounding.AwayFromZero) * 10;

    private static ImmutableArray<PressRun> Describe(PressRunConfig p) =>
    [
        new(1, "Proofreader", "#E8E6E0", "The standard run."),
        new(2, "First Edition", "#C53030", p.DailyBasePay == 0 ? "Dailies pay no base pay." : $"Dailies pay ${p.DailyBasePay} base pay."),
        new(3, "Late Edition", "#2F855A", $"Deadlines grow ×{p.WeeklyTargetGrowth} faster each week."),
        new(4, "Rush Job", "#2B6CB0", $"{-p.BossSubmissionsDelta} fewer submission in the Sunday Edition."),
        new(5, "Ink Shortage", "#6B46C1", $"{-p.DiscardsDelta} fewer discard per round."),
        new(6, "Heavy Printing", "#D69E2E", p.DeskItemPriceIncrease == 0
            ? $"Rerolls start ${p.RerollCostIncrease} higher."
            : $"Desk Items cost ${p.DeskItemPriceIncrease} more; rerolls start ${p.RerollCostIncrease} higher."),
        new(7, "Censored Press", "#DD6B20", "One letter is censored each round: it can't be placed."),
        new(8, "Final Print Run", "#B7791F", "Every Sunday boss adds a second editor's rule."),
    ];
}
