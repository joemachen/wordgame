using System.Collections.Immutable;

namespace Crossword.Core.Run;

/// <summary>
/// One setting of the Circulation dial: a print run of ×<see cref="PrintRun"/>. Deadlines ramp by
/// <see cref="WeekScales"/> (×1 in Week 1 up to ×<see cref="PrintRun"/> in the final week, back-loaded: week w of 5 is
/// ×PrintRun^((w/4)²), rounded to cents and written out so no floating point is involved); every paycheck's base pay gains <see cref="BasePayBonus"/> and the
/// Rare, Epic and Legendary weights of the shop's rarity roll are multiplied by <see cref="RarityBoost"/>.
/// <see cref="Color"/> is a hex color for the UI.
/// </summary>
public sealed record Circulation(int Level, string Name, decimal PrintRun, ImmutableArray<decimal> WeekScales, int BasePayBonus,
    decimal RarityBoost, string Color)
{
    /// <summary>The finale's deadline multiplier, as the player reads it ("×12").</summary>
    public string PrintRunText => $"×{PrintRun:0.##}";
}

/// <summary>
/// The Circulation dial (ROADMAP §3b phase 4): how big a print run the player signs up for, chosen at run start next to
/// the Press Run. Where a Press Run adds rules, Circulation scales the numbers: the early weeks play like the base game and
/// the finale's deadline is ×<see cref="Circulation.PrintRun"/>, so only an engine build (compounding Desk Items,
/// multiplicative tiles, Office upgrades) gets there; richer paychecks and rarer shops help build one. Level 1 (×1) is the
/// base game. Winning a run at level N unlocks N + 1 (<see cref="Profile.StatsQueries.UnlockedCirculation"/>, any deck and
/// Press Run). Like a deck or a Press Run it is a <see cref="RunConfig"/> transform (<see cref="Apply"/>); the run stores
/// its level in <see cref="Domain.RunState.Circulation"/>.
/// </summary>
public static class Circulations
{
    public const int Lowest = 1;

    public static ImmutableArray<Circulation> All { get; } =
    [
        new(1, "Local Weekly", 1, [1, 1, 1, 1, 1], BasePayBonus: 0, RarityBoost: 1, "#8A9BA8"),
        new(2, "County Paper", 3, [1, 1.07m, 1.32m, 1.86m, 3], BasePayBonus: 1, RarityBoost: 1.5m, "#4CC38A"),
        new(3, "City Daily", 12, [1, 1.17m, 1.86m, 4.05m, 12], BasePayBonus: 4, RarityBoost: 2, "#5AA9E6"),
        new(4, "Metro Edition", 50, [1, 1.28m, 2.66m, 9.03m, 50], BasePayBonus: 6, RarityBoost: 2.5m, "#A66CFF"),
        new(5, "National", 250, [1, 1.41m, 3.98m, 22.33m, 250], BasePayBonus: 8, RarityBoost: 3, "#F0A020"),
        new(6, "Global Syndicate", 1000, [1, 1.54m, 5.62m, 48.7m, 1000], BasePayBonus: 10, RarityBoost: 3.5m, "#E5484D"),
    ];

    public static int Highest => All.Length;

    public static bool IsLevel(int level) => level >= Lowest && level <= Highest;

    public static Circulation Get(int level) =>
        IsLevel(level) ? All[level - 1] : throw new ArgumentOutOfRangeException(nameof(level), level, "No such Circulation.");

    /// <summary>The print run of <paramref name="level"/> applied to <paramref name="config"/> (level 1 = the same instance).</summary>
    public static RunConfig Apply(RunConfig config, int level)
    {
        var c = Get(level);
        if (c.Level == Lowest)
            return config;
        var shop = config.Shop;
        return config with
        {
            WeekScales = c.WeekScales,
            Days = config.Days.Select(day => day with { BasePay = day.BasePay + c.BasePayBonus }).ToImmutableArray(),
            Shop = shop with
            {
                RareWeight = Boost(shop.RareWeight, c.RarityBoost),
                EpicWeight = Boost(shop.EpicWeight, c.RarityBoost),
                LegendaryWeight = Boost(shop.LegendaryWeight, c.RarityBoost),
            },
        };
    }

    /// <summary>One line for the New-run picker: the finale's deadline and the rewards.</summary>
    public static string Describe(Circulation c, RunConfig config) =>
        c.Level == Lowest
            ? $"The base game: Week {config.WeekTargets.Length} Sunday deadline {Scoring.ScoreFormat.Number(config.TargetFor(config.TotalRounds - 1))}."
            : $"Deadlines grow to {c.PrintRunText} by Week {config.WeekTargets.Length} (Sunday: "
              + $"{Scoring.ScoreFormat.Number(Apply(config, c.Level).TargetFor(config.TotalRounds - 1))}). "
              + $"+${c.BasePayBonus} on every paycheck; Rare and better Desk Items {c.RarityBoost:0.#}× as likely.";

    private static int Boost(int weight, decimal factor) => (int)decimal.Round(weight * factor, MidpointRounding.AwayFromZero);
}
