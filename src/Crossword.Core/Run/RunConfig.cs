using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
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
    int InterestCap = 5)
{
    /// <summary>Paid per discard left when the round is won (off by default; an economy lever under study).</summary>
    public int PerUnusedDiscard { get; init; }

    /// <summary>
    /// A won round's paycheck is topped up to at least this much (<see cref="Payout.FloorTopUp"/>).
    /// $5 since 2026-10-10 (user's choice, "money feels hard to make"): measured +4.5 pts of win rate for a weak
    /// player (ScoreFraction 0.6) and +0.5 at the reference 0.75, so it helps weak players without a target retune;
    /// base pay +$1 (+4 / +7) and a $6 floor (+4 / +5.5) would have needed one. 0 turns it off.
    /// </summary>
    public int MinPaycheck { get; init; } = 5;
}

/// <summary>
/// Shape of a run: <see cref="WeekTargets"/>.Length weeks × <see cref="Days"/> rounds. After the final week the
/// run is won; endless play continues with targets growing by <see cref="EndlessGrowth"/> per week.
/// Default targets tuned with <see cref="Analysis.RunSimulator"/>'s evaluating shop bot and the ScoreFraction skill
/// model (a human-like player; 150–200 runs per cell): raised ×1.3 on 2026-10-05 after balanced draws made hands
/// fairer, then ×1.15 after the richer economy (overkill every 25%, interest per $4), then ×0.88 on 2026-10-11 (user's
/// choice) after the content batch (Epic/Legendary items, rule-breaking Stationery) dropped the reference skill 0.75
/// from ~40% to ~33% wins; then steepened on 2026-10-12 for the compounding engines (multiplicative tile enhancements
/// and Desk Items): 450/1600/5200/17000/50000, Week 5 Sunday 80,000 (×2.4), reference 0.75 at ~36%. Retune with
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

    /// <summary>Boss rounds have at most this many submissions, after the boss (0 = no cap; Press Runs).</summary>
    public int BossSubmissionsCap { get; init; }

    /// <summary>Added to every round's discards after the boss (Press Runs; never below 0).</summary>
    public int DiscardsDelta { get; init; }

    /// <summary>
    /// Letters one of which is censored each round (can't be placed; see <see cref="RunRules.CensoredLetterFor"/>).
    /// Empty = no censoring (Press Runs).
    /// </summary>
    public string CensoredLetters { get; init; } = "";

    /// <summary>The week's boss adds one more Early/Mid boss's rule (<see cref="Reprint"/>; Press Runs).</summary>
    public bool BossExtraModifier { get; init; }

    /// <summary>With <see cref="BossExtraModifier"/>, the first week (0-based) whose boss gets the extra rule.</summary>
    public int BossExtraFromWeek { get; init; }

    /// <summary>Every round's minimum word length; a boss can raise it further (starting decks).</summary>
    public int MinWordLength { get; init; } = 2;

    /// <summary>Desk Item slots, at most <see cref="RunState.MaxDeskSlots"/> (starting decks, Office upgrades).</summary>
    public int DeskSlots { get; init; } = RunState.DefaultDeskSlots;

    /// <summary>Stationery slots, at most <see cref="RunState.MaxStationerySlots"/> (Office upgrades).</summary>
    public int StationerySlots { get; init; } = RunState.DefaultStationerySlots;

    /// <summary>Every round's board size before the boss (Tight Margins still shrinks it; Office upgrades).</summary>
    public int BoardSize { get; init; } = 7;

    /// <summary>Every round's premium pairs before the boss (null = <see cref="PremiumPairs.Default"/>; Office upgrades).</summary>
    public PremiumPairs? Premiums { get; init; }

    /// <summary>Added to every round's hand size, after the boss (Office upgrades).</summary>
    public int HandSizeDelta { get; init; }

    /// <summary>Added to every round's submissions after the boss and the boss cap (Office upgrades).</summary>
    public int SubmissionsDelta { get; init; }

    /// <summary>The shop offers Desk Items already owned and the desk holds copies (Office upgrades).</summary>
    public bool DuplicateDeskItems { get; init; }

    /// <summary>The numbers behind the Office upgrades (<see cref="OfficeUpgrades"/>).</summary>
    public OfficeConfig Office { get; init; } = OfficeConfig.Default;

    /// <summary>Desk Items a new run starts with, left to right (starting decks).</summary>
    public ImmutableArray<IDeskItem> StartingDeskItems { get; init; } = ImmutableArray<IDeskItem>.Empty;

    /// <summary>Boss ids that never appear, in any week (starting decks).</summary>
    public ImmutableArray<string> ExcludedBosses { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Every deadline is multiplied by this, after the week table and the day multiplier (1 = unchanged). The hook for
    /// the Circulation dial: a run that asks for a bigger print run gets steeper deadlines and bigger rewards.
    /// </summary>
    public decimal TargetScale { get; init; } = 1;

    public static RunConfig Default { get; } = new(
        WeekTargets: [450, 1600, 5200, 17000, 50000],
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
    /// final week) draw from every boss so late builds keep meeting variety. <see cref="ExcludedBosses"/> are left out.
    /// </summary>
    public ImmutableArray<BossModifier> BossPoolFor(int week)
    {
        var pool = week >= WeekTargets.Length || BossTiers.IsDefaultOrEmpty
            ? BossCatalog.All
            : BossTiers.Last(t => t.FirstWeek <= week).Bosses;
        return ExcludedBosses.IsDefaultOrEmpty ? pool : pool.Where(boss => !ExcludedBosses.Contains(boss.Id)).ToImmutableArray();
    }

    public int TotalRounds => WeekTargets.Length * RoundsPerWeek;

    public int WeekOf(int roundIndex) => roundIndex / RoundsPerWeek;

    public RoundKind KindOf(int roundIndex) => Days[roundIndex % RoundsPerWeek];

    public bool IsFinalRound(int roundIndex) => roundIndex == TotalRounds - 1;

    /// <summary>
    /// The deadline for a round: the week's table entry (endless weeks grow the last one by <see cref="EndlessGrowth"/>
    /// per week, in exact decimal steps) × the day's multiplier × <see cref="TargetScale"/>. Saturates at
    /// <see cref="ScoreMath.MaxScore"/> instead of overflowing deep into endless play.
    /// </summary>
    public long TargetFor(int roundIndex)
    {
        int week = WeekOf(roundIndex);
        long weekTarget = WeekTargets[Math.Min(week, WeekTargets.Length - 1)];
        for (int endless = WeekTargets.Length - 1; endless < week && weekTarget < ScoreMath.MaxScore; endless++)
            weekTarget = ScoreMath.Scale(weekTarget, EndlessGrowth);
        long target = ScoreMath.Scale(weekTarget, KindOf(roundIndex).TargetMultiplier);
        return TargetScale == 1 ? target : ScoreMath.Scale(target, TargetScale);
    }

    /// <summary>
    /// Round rules for a round; <paramref name="boss"/> is applied only on boss rounds, on top of the run's board size and
    /// premiums. The boss submission cap and the deltas apply after the boss (a cap of 3 leaves Tight Deadline's 3
    /// submissions alone; discards stack), and <see cref="SubmissionsDelta"/> after the cap (an Overtime submission is
    /// paid for, like Extra! Extra!'s); the minimum word length is the higher of the run's and the boss's.
    /// </summary>
    public RoundConfig RoundConfigFor(int roundIndex, BossModifier? boss = null, char? censoredLetter = null)
    {
        var config = new RoundConfig(TargetScore: TargetFor(roundIndex), BoardSize: BoardSize, Premiums: Premiums, Draw: Draw,
            CensoredLetter: censoredLetter);
        bool bossRound = KindOf(roundIndex).IsBoss;
        if (bossRound && boss is not null)
            config = boss.Apply(config);
        if (config.MinWordLength < MinWordLength)
            config = config with { MinWordLength = MinWordLength };
        int submissionsDelta = (bossRound && BossSubmissionsCap > 0 ? Math.Min(0, BossSubmissionsCap - config.Submissions) : 0)
            + SubmissionsDelta;
        return submissionsDelta == 0 && DiscardsDelta == 0 && HandSizeDelta == 0
            ? config
            : config with
            {
                Submissions = Math.Max(1, config.Submissions + submissionsDelta),
                Discards = Math.Max(0, config.Discards + DiscardsDelta),
                HandSize = Math.Max(1, config.HandSize + HandSizeDelta),
            };
    }
}
