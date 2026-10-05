using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Rules;

public class RoundRulesTests
{
    private static readonly IReadOnlyList<IDeskItem> NoItems = [];

    /// <summary>Round with a known hand ("CATSORE") and a known bag, on a plain 5×5 board.</summary>
    private static RoundState KnownRound(long target = 1000, int submissions = 4, int discards = 3)
    {
        var hand = HandOf("CATSORE");
        var bag = new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('E'))).ToImmutableArray());
        return new RoundState(
            new RoundConfig(TargetScore: target, BoardSize: 5, Submissions: submissions, Discards: discards),
            Board.Empty(5), bag, hand, Rng.FromSeed(5), Score: 0, SubmissionsLeft: submissions, DiscardsLeft: discards);
    }

    private static Result<SubmitOutcome, RoundError> PlayCat(RoundState round) =>
        RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"), Words, NoItems, ScoringConfig.Default);

    [Fact]
    public void Start_DealsHand_FromFullDeck_OnSymmetricBoard()
    {
        var run = RunState.New(seed: 7);

        var (round, nextRun) = RoundRules.Start(run, RoundConfig.ForRound(0), LexiconLoader.Enable);

        Assert.Equal(7, round.Board.Size);
        Assert.True(round.Board.IsEmpty);
        Assert.Equal(7, round.Hand.Count);
        Assert.Equal(run.Deck.Length - 7, round.Bag.Count);
        Assert.Equal(RoundStatus.InProgress, round.Status);
        Assert.NotEqual(run.Rng, nextRun.Rng);
    }

    [Fact]
    public void Start_IsDeterministicPerSeed()
    {
        var (a, _) = RoundRules.Start(RunState.New(42), RoundConfig.ForRound(0), LexiconLoader.Enable);
        var (b, _) = RoundRules.Start(RunState.New(42), RoundConfig.ForRound(0), LexiconLoader.Enable);
        var (c, _) = RoundRules.Start(RunState.New(43), RoundConfig.ForRound(0), LexiconLoader.Enable);

        Assert.Equal(a.Hand.Tiles.Select(t => t.Id), b.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(a.Board.Premiums, b.Board.Premiums);
        Assert.NotEqual(a.Board.Premiums, c.Board.Premiums);
    }

    [Fact]
    public void Submit_Valid_ScoresPlacesRefillsAndSpendsSubmission()
    {
        var round = KnownRound();

        var outcome = PlayCat(round).Value;
        var next = outcome.State;

        Assert.Equal(10, outcome.Score.Total);
        Assert.Equal(10, next.Score);
        Assert.Equal(3, next.SubmissionsLeft);
        Assert.Equal(3, next.DiscardsLeft);
        Assert.Equal(7, next.Hand.Count);
        Assert.Equal(17, next.Bag.Count);
        Assert.Equal('C', next.Board.TileAt(new Position(0, 0))!.Letter.Char);
        Assert.DoesNotContain(next.Hand.Tiles, t => t.Id is 0 or 1 or 2);
    }

    [Fact]
    public void Submit_Invalid_ReturnsError_AndConsumesNothing()
    {
        var round = KnownRound();
        var placed = Spell(round.Board, round.Hand, 0, 0, Direction.Across, "TOC");

        var result = RoundRules.Submit(round, placed, Words, NoItems, ScoringConfig.Default);

        var error = Assert.IsType<RoundError.InvalidPlacement>(result.Error);
        Assert.IsType<PlacementError.InvalidWords>(error.Error);
        Assert.Equal(4, round.SubmissionsLeft);
        Assert.True(round.Board.IsEmpty);
    }

    [Fact]
    public void Submit_ReachingTarget_WinsRound()
    {
        var next = PlayCat(KnownRound(target: 10)).Value.State;

        Assert.Equal(RoundStatus.Won, next.Status);
    }

    [Fact]
    public void Submit_LastSubmissionBelowTarget_LosesRound()
    {
        var next = PlayCat(KnownRound(target: 1000, submissions: 1)).Value.State;

        Assert.Equal(RoundStatus.Lost, next.Status);
    }

    [Fact]
    public void Submit_AfterRoundOver_IsRejected()
    {
        var won = PlayCat(KnownRound(target: 10)).Value.State;

        var result = RoundRules.Submit(won, Spell(won.Board, won.Hand, 1, 0, Direction.Down, "SO"), Words, NoItems, ScoringConfig.Default);

        Assert.IsType<RoundError.RoundOver>(result.Error);
    }

    [Fact]
    public void Discard_RemovesTiles_Refills_AndSpendsDiscardOnly()
    {
        var round = KnownRound();

        var next = RoundRules.Discard(round, [0, 1], Words).Value;

        Assert.Equal(7, next.Hand.Count);
        Assert.False(next.Hand.Contains(0));
        Assert.False(next.Hand.Contains(1));
        Assert.Equal(2, next.DiscardsLeft);
        Assert.Equal(4, next.SubmissionsLeft);
        Assert.Equal(18, next.Bag.Count);
    }

    [Fact]
    public void Discard_WithNoneLeft_IsRejected()
    {
        Assert.IsType<RoundError.NoDiscardsLeft>(RoundRules.Discard(KnownRound(discards: 0), [0], Words).Error);
    }

    [Fact]
    public void Discard_EmptySelection_IsRejected()
    {
        Assert.IsType<RoundError.NothingSelected>(RoundRules.Discard(KnownRound(), [], Words).Error);
    }

    [Fact]
    public void Discard_TileNotInHand_IsRejected()
    {
        var error = Assert.IsType<RoundError.TileNotInHand>(RoundRules.Discard(KnownRound(), [0, 55], Words).Error);
        Assert.Equal(55, error.TileId);
    }

    [Fact]
    public void SameSeedAndInputs_ProduceIdenticalRounds()
    {
        static RoundState Replay()
        {
            var (round, _) = RoundRules.Start(RunState.New(99), RoundConfig.ForRound(0), LexiconLoader.Enable);
            return RoundRules.Discard(round, round.Hand.Tiles.Take(3).Select(t => t.Id).ToArray(), LexiconLoader.Enable).Value;
        }

        var a = Replay();
        var b = Replay();

        Assert.Equal(a.Hand.Tiles.Select(t => t.Id), b.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(a.Rng, b.Rng);
    }

    [Fact]
    public void Discard_IntoUnplayableHand_WithNoDiscardsLeft_Deadlocks()
    {
        // Bag holds only Qs: after discarding the whole hand, nothing is playable and no discards remain.
        var qs = new TileBag(Enumerable.Range(100, 10).Select(i => new Tile(i, Letter.From('Q'))).ToImmutableArray());
        var round = KnownRound(discards: 1) with { Bag = qs };

        var next = RoundRules.Discard(round, round.Hand.Tiles.Select(t => t.Id).ToArray(), Words).Value;

        Assert.True(next.Deadlocked);
        Assert.Equal(RoundStatus.Lost, next.Status);
    }

    [Fact]
    public void UnplayableHand_WithDiscardsLeft_IsNotDeadlocked()
    {
        var qs = new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('Q'))).ToImmutableArray());
        var round = KnownRound(discards: 2) with { Bag = qs };

        var next = RoundRules.Discard(round, round.Hand.Tiles.Select(t => t.Id).ToArray(), Words).Value;

        Assert.False(RoundRules.HasLegalPlay(next, Words));
        Assert.False(next.Deadlocked);
        Assert.Equal(RoundStatus.InProgress, next.Status);
    }
}
