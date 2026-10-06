using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Scoring;

namespace Crossword.Core.Rules;

/// <summary>Why a round action was rejected. <see cref="Message"/> is player-facing.</summary>
public abstract record RoundError
{
    public abstract string Message { get; }

    public sealed record RoundOver(RoundStatus Status) : RoundError
    {
        public override string Message => $"The round is over ({Status}).";
    }

    public sealed record NoDiscardsLeft : RoundError
    {
        public override string Message => "No discards left this round.";
    }

    public sealed record NothingSelected : RoundError
    {
        public override string Message => "Select at least one tile.";
    }

    public sealed record TileNotInHand(int TileId) : RoundError
    {
        public override string Message => $"Tile #{TileId} is not in your hand.";
    }

    public sealed record NoTileThere(Position Position) : RoundError
    {
        public override string Message => "There's no tile there.";
    }

    public sealed record InvalidPlacement(PlacementError Error) : RoundError
    {
        public override string Message => Error.Message;
    }
}

public sealed record SubmitOutcome(RoundState State, ScoreContext Score);

/// <summary>
/// Pure round transitions: start, submit a play, discard (plus Stationery's redraw and tile removal). Each transition
/// ends by checking for a deadlock: no legal play for the hand and no discards left means the round is lost.
/// </summary>
public static class RoundRules
{
    /// <summary>
    /// Starts a round from the run's full deck. The round gets its own RNG stream derived from the run RNG,
    /// so the run RNG advances by exactly one step per round regardless of how the round is played.
    /// </summary>
    public static (RoundState Round, RunState Run) Start(RunState run, RoundConfig config, IWordGraph lexicon)
    {
        var (roundSeed, runRng) = run.Rng.NextUInt64();
        var rng = Rng.FromSeed(roundSeed);

        (var premiums, rng) = PremiumLayout.Generate(config.BoardSize, config.PremiumPairs, rng);
        var board = Board.Empty(config.BoardSize, premiums);
        if (config.BlockedPairs > 0)
        {
            (var blocked, rng) = PremiumLayout.GenerateBlocked(config.BoardSize, config.BlockedPairs, premiums, rng);
            board = board with { Blocked = blocked };
        }

        var round = new RoundState(
            Config: config,
            Board: board,
            Bag: new TileBag(run.Deck),
            Hand: Hand.Empty,
            Rng: rng,
            Score: 0,
            SubmissionsLeft: config.Submissions,
            DiscardsLeft: config.Discards);

        return (CheckDeadlock(DrawRules.DrawToHandSize(round), lexicon), run with { Rng = runRng });
    }

    public static Result<SubmitOutcome, RoundError> Submit(
        RoundState state,
        IReadOnlyList<PlacedTile> placed,
        IWordGraph lexicon,
        IReadOnlyList<IDeskItem> deskItems,
        ScoringConfig scoring,
        int moneyHeld = 0,
        bool canEscape = false)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<SubmitOutcome, RoundError>.Fail(new RoundError.RoundOver(state.Status));

        var validation = PlacementValidator.Validate(state.Board, state.Hand, placed, lexicon, state.Config.MinWordLength,
            state.Config.CensoredLetter);
        if (!validation.IsOk)
            return Result<SubmitOutcome, RoundError>.Fail(new RoundError.InvalidPlacement(validation.Error));

        var play = validation.Value;
        var score = ScoringEngine.Score(play, deskItems, state.Config.EffectiveScoring(scoring), Environment(state, moneyHeld));
        var next = state with
        {
            Board = play.BoardAfter,
            Hand = state.Hand.Remove(placed.Select(p => p.Tile.Id)),
            Score = state.Score + score.Total,
            SubmissionsLeft = state.SubmissionsLeft - 1,
            SubmissionsMade = state.SubmissionsMade + 1,
            WordsFormed = state.WordsFormed.Union(play.Words.Select(w => w.Text)),
        };

        return Result<SubmitOutcome, RoundError>.Ok(new SubmitOutcome(CheckDeadlock(DrawRules.DrawToHandSize(next), lexicon, canEscape), score));
    }

    /// <summary>Removes the selected tiles from play for this round and refills the hand. Costs one discard.</summary>
    public static Result<RoundState, RoundError> Discard(RoundState state, IReadOnlyCollection<int> tileIds, IWordGraph lexicon,
        bool canEscape = false)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<RoundState, RoundError>.Fail(new RoundError.RoundOver(state.Status));
        if (state.DiscardsLeft <= 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.NoDiscardsLeft());
        return Replace(state with { DiscardsLeft = state.DiscardsLeft - 1 }, tileIds, lexicon, canEscape);
    }

    /// <summary>Like <see cref="Discard"/> but free (Scissors): the tiles leave play for the round and are redrawn.</summary>
    public static Result<RoundState, RoundError> Redraw(RoundState state, IReadOnlyCollection<int> tileIds, IWordGraph lexicon,
        bool canEscape = false)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<RoundState, RoundError>.Fail(new RoundError.RoundOver(state.Status));
        return Replace(state, tileIds, lexicon, canEscape);
    }

    /// <summary>Takes a tile off the board for the rest of the round (White-Out). The tile is not returned.</summary>
    public static Result<RoundState, RoundError> RemoveTile(RoundState state, Position position, IWordGraph lexicon,
        bool canEscape = false)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<RoundState, RoundError>.Fail(new RoundError.RoundOver(state.Status));
        if (!state.Board.IsOccupied(position))
            return Result<RoundState, RoundError>.Fail(new RoundError.NoTileThere(position));
        return Result<RoundState, RoundError>.Ok(CheckDeadlock(state with { Board = state.Board.Remove(position) }, lexicon, canEscape));
    }

    /// <summary>What Desk Items can see about the round when the next play is scored.</summary>
    public static ScoreEnvironment Environment(RoundState state, int moneyHeld) =>
        new(moneyHeld, state.SubmissionsLeft, state.DiscardsLeft)
        {
            SubmissionsMade = state.SubmissionsMade,
            WordsFormed = state.WordsFormed,
        };

    /// <summary>True if the current hand has at least one legal play on the current board.</summary>
    public static bool HasLegalPlay(RoundState state, IWordGraph lexicon) =>
        MoveGenerator.HasLegalPlay(state.Board, state.Hand, lexicon, state.Config.MinWordLength, state.Config.CensoredLetter);

    /// <summary>
    /// Marks the round deadlocked when no legal play exists and no discards remain, unless <paramref name="canEscape"/>
    /// (held Stationery can still change the hand or board). Runs after every transition.
    /// </summary>
    public static RoundState CheckDeadlock(RoundState state, IWordGraph lexicon, bool canEscape = false) =>
        !canEscape && state.Status == RoundStatus.InProgress && state.DiscardsLeft == 0 && !HasLegalPlay(state, lexicon)
            ? state with { Deadlocked = true }
            : state;

    private static Result<RoundState, RoundError> Replace(RoundState state, IReadOnlyCollection<int> tileIds, IWordGraph lexicon,
        bool canEscape)
    {
        if (tileIds.Count == 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.NothingSelected());
        if (tileIds.FirstOrDefault(id => !state.Hand.Contains(id), -1) is var missing and >= 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.TileNotInHand(missing));

        var next = state with { Hand = state.Hand.Remove(tileIds) };
        return Result<RoundState, RoundError>.Ok(CheckDeadlock(DrawRules.DrawToHandSize(next), lexicon, canEscape));
    }
}
