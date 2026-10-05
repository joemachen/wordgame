using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Scoring;

namespace Crossword.Core.Run;

/// <summary>One slot in the week, e.g. "Daily" or the "Sunday Edition" boss.</summary>
public sealed record RoundKind(string Name, decimal TargetMultiplier, int BasePay, bool IsBoss);

/// <summary>Money rules. Overkill rewards a big finishing play; both bonuses are capped to limit snowballing.</summary>
public sealed record EconomyConfig(
    int StartingMoney = 4,
    int PerUnusedSubmission = 1,
    decimal OverkillStep = 0.5m,
    int OverkillCap = 3,
    int InterestPer = 5,
    int InterestCap = 5);

/// <summary>
/// Shape of a run: <see cref="WeekTargets"/>.Length weeks × <see cref="Days"/> rounds. After the final week the
/// run is won; endless play continues with targets growing by <see cref="EndlessGrowth"/> per week.
/// Default targets were tuned with <see cref="Analysis.RunSimulator"/>'s naive shop bot (skill 0.9 wins ~42%,
/// 0.8 ~29%). The evaluating shop bot wins ~97% / 83% / 59% at skill 0.9 / 0.8 / 0.7, so these targets are too
/// soft for a player who shops well. Retune with 'runsim' whenever scaling content changes.
/// </summary>
public sealed record RunConfig(
    ImmutableArray<long> WeekTargets,
    ImmutableArray<RoundKind> Days,
    EconomyConfig Economy,
    ScoringConfig Scoring,
    ShopConfig Shop,
    decimal EndlessGrowth = 2)
{
    public static RunConfig Default { get; } = new(
        WeekTargets: [150, 400, 900, 1900, 3800],
        Days:
        [
            new RoundKind("Daily", 1m, BasePay: 3, IsBoss: false),
            new RoundKind("Saturday Stumper", 1.3m, BasePay: 4, IsBoss: false),
            new RoundKind("Sunday Edition", 1.6m, BasePay: 5, IsBoss: true),
        ],
        Economy: new EconomyConfig(),
        Scoring: ScoringConfig.Default,
        Shop: ShopConfig.Default);

    public int RoundsPerWeek => Days.Length;

    public int TotalRounds => WeekTargets.Length * RoundsPerWeek;

    public int WeekOf(int roundIndex) => roundIndex / RoundsPerWeek;

    public RoundKind KindOf(int roundIndex) => Days[roundIndex % RoundsPerWeek];

    public bool IsFinalRound(int roundIndex) => roundIndex == TotalRounds - 1;

    public long TargetFor(int roundIndex)
    {
        int week = WeekOf(roundIndex);
        decimal weekTarget = week < WeekTargets.Length
            ? WeekTargets[week]
            : WeekTargets[^1] * (decimal)Math.Pow((double)EndlessGrowth, week - (WeekTargets.Length - 1));
        return (long)(weekTarget * KindOf(roundIndex).TargetMultiplier);
    }

    /// <summary>Round rules for a round; <paramref name="boss"/> is applied only on boss rounds.</summary>
    public RoundConfig RoundConfigFor(int roundIndex, BossModifier? boss = null)
    {
        var config = new RoundConfig(TargetScore: TargetFor(roundIndex));
        return KindOf(roundIndex).IsBoss && boss is not null ? boss.Apply(config) : config;
    }
}
