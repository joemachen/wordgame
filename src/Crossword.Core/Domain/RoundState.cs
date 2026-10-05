using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Core.Domain;

/// <summary>Rules for one round. <see cref="Boss"/> (Sunday rounds) has already been applied to the other fields.</summary>
public sealed record RoundConfig(
    long TargetScore,
    int BoardSize = 7,
    int Submissions = 4,
    int Discards = 3,
    int HandSize = 7,
    PremiumPairs? Premiums = null,
    int MinWordLength = 2,
    int BlockedPairs = 0,
    BossModifier? Boss = null,
    decimal BonusMult = 0)
{
    public PremiumPairs PremiumPairs => Premiums ?? PremiumPairs.Default;

    /// <summary>The scoring rules in effect this round (the boss may alter them; Red Ink Bottle adds <see cref="BonusMult"/>).</summary>
    public ScoringConfig EffectiveScoring(ScoringConfig scoring)
    {
        var effective = Boss?.ModifyScoring(scoring) ?? scoring;
        return BonusMult == 0 ? effective : effective with { BonusMult = effective.BonusMult + BonusMult };
    }
}

public enum RoundStatus
{
    InProgress,
    Won,
    Lost,
}

/// <summary>
/// Immutable state of the round in progress. The board persists across submissions within it.
/// <see cref="Deadlocked"/> is set by RoundRules when no legal play exists and no discards remain.
/// </summary>
public sealed record RoundState(
    RoundConfig Config,
    Board Board,
    TileBag Bag,
    Hand Hand,
    Rng Rng,
    long Score,
    int SubmissionsLeft,
    int DiscardsLeft,
    bool Deadlocked = false)
{
    public RoundStatus Status =>
        Score >= Config.TargetScore ? RoundStatus.Won
        : SubmissionsLeft <= 0 || Deadlocked || (Hand.Count == 0 && Bag.IsEmpty) ? RoundStatus.Lost
        : RoundStatus.InProgress;
}
