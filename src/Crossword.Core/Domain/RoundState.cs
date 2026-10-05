using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>Rules for one round (blind). Values from <see cref="ForRound"/> are PLACEHOLDERS.</summary>
public sealed record RoundConfig(
    long TargetScore,
    int BoardSize = 7,
    int Submissions = 4,
    int Discards = 3,
    int HandSize = 7,
    PremiumPairs? Premiums = null)
{
    public PremiumPairs PremiumPairs => Premiums ?? PremiumPairs.Default;

    /// <summary>Placeholder progression: target grows 50% per round.</summary>
    public static RoundConfig ForRound(int roundIndex) =>
        new(TargetScore: (long)(150 * Math.Pow(1.5, roundIndex)));
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
