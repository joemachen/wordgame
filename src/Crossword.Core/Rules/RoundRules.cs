using System.Collections.Immutable;
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

    public sealed record InvalidPlacement(PlacementError Error) : RoundError
    {
        public override string Message => Error.Message;
    }
}

public sealed record SubmitOutcome(RoundState State, ScoreContext Score);

/// <summary>Pure round transitions: start, submit a play, discard.</summary>
public static class RoundRules
{
    /// <summary>
    /// Starts a round from the run's full deck. The round gets its own RNG stream derived from the run RNG,
    /// so the run RNG advances by exactly one step per round regardless of how the round is played.
    /// </summary>
    public static (RoundState Round, RunState Run) Start(RunState run, RoundConfig config)
    {
        var (roundSeed, runRng) = run.Rng.NextUInt64();
        var rng = Rng.FromSeed(roundSeed);

        (var premiums, rng) = PremiumLayout.Generate(config.BoardSize, config.PremiumPairs, rng);
        var round = new RoundState(
            Config: config,
            Board: Board.Empty(config.BoardSize, premiums),
            Bag: new TileBag(run.Deck),
            Hand: Hand.Empty,
            Rng: rng,
            Score: 0,
            SubmissionsLeft: config.Submissions,
            DiscardsLeft: config.Discards);

        return (DrawRules.DrawToHandSize(round), run with { Rng = runRng });
    }

    public static Result<SubmitOutcome, RoundError> Submit(
        RoundState state,
        IReadOnlyList<PlacedTile> placed,
        ILexicon lexicon,
        IReadOnlyList<IDeskItem> deskItems,
        ScoringConfig scoring)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<SubmitOutcome, RoundError>.Fail(new RoundError.RoundOver(state.Status));

        var validation = PlacementValidator.Validate(state.Board, state.Hand, placed, lexicon);
        if (!validation.IsOk)
            return Result<SubmitOutcome, RoundError>.Fail(new RoundError.InvalidPlacement(validation.Error));

        var play = validation.Value;
        var score = ScoringEngine.Score(play, deskItems, scoring);
        var next = state with
        {
            Board = play.BoardAfter,
            Hand = state.Hand.Remove(placed.Select(p => p.Tile.Id)),
            Score = state.Score + score.Total,
            SubmissionsLeft = state.SubmissionsLeft - 1,
        };

        return Result<SubmitOutcome, RoundError>.Ok(new SubmitOutcome(DrawRules.DrawToHandSize(next), score));
    }

    /// <summary>Removes the selected tiles from play for this round and refills the hand. Costs one discard.</summary>
    public static Result<RoundState, RoundError> Discard(RoundState state, IReadOnlyCollection<int> tileIds)
    {
        if (state.Status != RoundStatus.InProgress)
            return Result<RoundState, RoundError>.Fail(new RoundError.RoundOver(state.Status));
        if (state.DiscardsLeft <= 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.NoDiscardsLeft());
        if (tileIds.Count == 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.NothingSelected());
        if (tileIds.FirstOrDefault(id => !state.Hand.Contains(id), -1) is var missing and >= 0)
            return Result<RoundState, RoundError>.Fail(new RoundError.TileNotInHand(missing));

        var next = state with
        {
            Hand = state.Hand.Remove(tileIds),
            DiscardsLeft = state.DiscardsLeft - 1,
        };
        return Result<RoundState, RoundError>.Ok(DrawRules.DrawToHandSize(next));
    }
}
