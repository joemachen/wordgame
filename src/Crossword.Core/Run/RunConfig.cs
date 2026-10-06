using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Scoring;

namespace Crossword.Core.Run;

/// <summary>One slot in the week, e.g. "Daily" or the "Sunday Edition" boss.</summary>
public sealed record RoundKind(string Name, decimal TargetMultiplier, int BasePay, bool IsBoss);

/// <summary>
/// Money rules. Overkill rewards a big finishing play; both bonuses are capped to limit snowballing. Since 2026-10-05
/// overkill pays every 25% over the deadline (was 50%) and interest is $1 per $4 held (was $5): the bot earned only
/// $0.16/round of interest and money felt tight after the ×1.3 target raise.
/// </summary>
public sealed record EconomyConfig(
    int StartingMoney = 4,
    int PerUnusedSubmission = 1,
    decimal OverkillStep = 0.25m,
    int OverkillCap = 3,
    int InterestPer = 4,
    int InterestCap = 5);

/// <summary>
/// Shape of a run: <see cref="WeekTargets"/>.Length weeks × <see cref="Days"/> rounds. After the final week the
/// run is won; endless play continues with targets growing by <see cref="EndlessGrowth"/> per week.
/// Default targets tuned with <see cref="Analysis.RunSimulator"/>'s evaluating shop bot and the ScoreFraction skill
/// model (a human-like player; 150–200 runs per cell): raised ×1.3 on 2026-10-05 after balanced draws made hands
/// fairer, then ×1.15 after the richer economy (overkill every 25%, interest per $4) — the reference skill 0.75 wins
/// ~36%, won rounds take ~2.6 submissions (55% in 1–2). Retune with 'runsim [runs] [skill] frac' whenever scaling
/// content changes.
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

    /// <summary>Added to boss rounds' submissions after the boss (Press Runs; at least 1 remains).</summary>
    public int BossSubmissionsDelta { get; init; }

    /// <summary>Added to every round's discards after the boss (Press Runs; never below 0).</summary>
    public int DiscardsDelta { get; init; }

    /// <summary>
    /// Letters one of which is censored each round (can't be placed; see <see cref="RunRules.CensoredLetterFor"/>).
    /// Empty = no censoring (Press Runs).
    /// </summary>
    public string CensoredLetters { get; init; } = "";

    /// <summary>The week's boss adds one more Early/Mid boss's rule (<see cref="Reprint"/>; Press Runs).</summary>
    public bool BossExtraModifier { get; init; }

    public static RunConfig Default { get; } = new(
        WeekTargets: [510, 1790, 5380, 14580, 23920],
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

    /// <summary>
    /// Round rules for a round; <paramref name="boss"/> is applied only on boss rounds. The submission and discard
    /// deltas apply after the boss, so they stack with it (Tight Deadline's 3 submissions minus 1 = 2).
    /// </summary>
    public RoundConfig RoundConfigFor(int roundIndex, BossModifier? boss = null, char? censoredLetter = null)
    {
        var config = new RoundConfig(TargetScore: TargetFor(roundIndex), Draw: Draw, CensoredLetter: censoredLetter);
        bool bossRound = KindOf(roundIndex).IsBoss;
        if (bossRound && boss is not null)
            config = boss.Apply(config);
        int submissionsDelta = bossRound ? BossSubmissionsDelta : 0;
        return submissionsDelta == 0 && DiscardsDelta == 0
            ? config
            : config with
            {
                Submissions = Math.Max(1, config.Submissions + submissionsDelta),
                Discards = Math.Max(0, config.Discards + DiscardsDelta),
            };
    }
}
