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
/// Default targets tuned with <see cref="Analysis.RunSimulator"/>'s evaluating shop bot and the ScoreFraction skill
/// model (a human-like player; 150–200 runs per cell): raised ×1.3 on 2026-10-05 after balanced draws made hands
/// fairer — skill 0.9 / 0.75 / 0.6 wins ~59% / 36% / 15%, won rounds take ~2.6 submissions (55% in 1–2). Retune with
/// 'runsim [runs] [skill] frac' whenever scaling content changes.
/// </summary>
public sealed record RunConfig(
    ImmutableArray<long> WeekTargets,
    ImmutableArray<RoundKind> Days,
    EconomyConfig Economy,
    ScoringConfig Scoring,
    ShopConfig Shop,
    decimal EndlessGrowth = 2)
{
    /// <summary>How tiles are drawn into the hand each round (balanced by default; see <see cref="DrawConfig"/>).</summary>
    public DrawConfig Draw { get; init; } = DrawConfig.Balanced;

    /// <summary>The deck a new run starts with.</summary>
    public ImmutableArray<Tile> StartingTiles { get; init; } = StartingDeck.Create();

    /// <summary>Boss pools by week (see <see cref="BossPoolFor"/>). Ordered by ascending <see cref="BossTier.FirstWeek"/>.</summary>
    public ImmutableArray<BossTier> BossTiers { get; init; } = BossCatalog.DefaultTiers;

    public static RunConfig Default { get; } = new(
        WeekTargets: [440, 1560, 4680, 12680, 20800],
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

    /// <summary>
    /// The bosses that can appear in <paramref name="week"/>: the last tier that has started. Endless weeks (after the
    /// final week) draw from every boss so late builds keep meeting variety.
    /// </summary>
    public ImmutableArray<BossModifier> BossPoolFor(int week) =>
        week >= WeekTargets.Length || BossTiers.IsDefaultOrEmpty
            ? BossCatalog.All
            : BossTiers.Last(t => t.FirstWeek <= week).Bosses;

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
        var config = new RoundConfig(TargetScore: TargetFor(roundIndex), Draw: Draw);
        return KindOf(roundIndex).IsBoss && boss is not null ? boss.Apply(config) : config;
    }
}
