using System.Collections.Immutable;
using Crossword.Core.Effects;
using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Core.Domain;

/// <summary>A board word chosen with a Clipping: its letter chips count again on the next play (read from the board then).</summary>
public sealed record ClippedWord(Position Start, Direction Direction, string Text);

/// <summary>A hand tile marked with a Highlighter: its letter value counts <see cref="Factor"/> times on the next play it is in.</summary>
public sealed record TileHighlight(int TileId, int Factor);

/// <summary>
/// What a Correction Tape restores: the round, Desk Items and money as they were before the last play. Set by the play
/// and cleared by every other transition, so only the play just made can be taken back.
/// </summary>
public sealed record UndoPoint(RoundState Round, ImmutableArray<IDeskItem> DeskItems, int Money);

/// <summary>
/// Rules for one round. <see cref="Boss"/> (Sunday rounds) has already been applied to the other fields.
/// <see cref="Draw"/> null = plain uniform draws (runs set <see cref="Run.RunConfig.Draw"/>).
/// <see cref="CensoredLetter"/> can't be placed this round, not even by a wild tile (Censored Press).
/// One-play Stationery effects also live here: <see cref="Clipping"/> and <see cref="Highlight"/> end with the next
/// play, <see cref="IllegalWordsAllowed"/> (Poetic License) is spent by a play that uses it.
/// </summary>
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
    decimal BonusMult = 0,
    DrawConfig? Draw = null,
    char? CensoredLetter = null,
    ClippedWord? Clipping = null,
    TileHighlight? Highlight = null,
    int IllegalWordsAllowed = 0)
{
    public PremiumPairs PremiumPairs => Premiums ?? PremiumPairs.Default;

    /// <summary>
    /// The scoring rules in effect this round: the boss may alter them, Red Ink Bottle adds <see cref="BonusMult"/>,
    /// and a Clipping or Highlighter is scored on the next play.
    /// </summary>
    public ScoringConfig EffectiveScoring(ScoringConfig scoring)
    {
        var effective = Boss?.ModifyScoring(scoring) ?? scoring;
        if (BonusMult != 0)
            effective = effective with { BonusMult = effective.BonusMult + BonusMult };
        if (Clipping is not null || Highlight is not null)
            effective = effective with { Clipping = Clipping, Highlight = Highlight };
        return effective;
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
    /// <summary>Plays submitted so far this round.</summary>
    public int SubmissionsMade { get; init; }

    /// <summary>Every word formed by this round's earlier plays (main and cross words, by text).</summary>
    public ImmutableHashSet<string> WordsFormed { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>The state a Correction Tape would restore; null unless the last transition was a play.</summary>
    public UndoPoint? Undo { get; init; }

    public RoundStatus Status =>
        Score >= Config.TargetScore ? RoundStatus.Won
        : SubmissionsLeft <= 0 || Deadlocked || (Hand.Count == 0 && Bag.IsEmpty) ? RoundStatus.Lost
        : RoundStatus.InProgress;
}
