using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Analysis;

[Trait("Category", "Analysis")]
public class StationeryBotTests
{
    private static readonly RunConfig Config = RunConfig.Default with { Scoring = ScoringConfig.Default };

    /// <summary>A Daily round on a 5×5 board with CAT across the top, hand CATSORE and a bag of Es.</summary>
    private static GameSession Round(long target, int submissionsLeft, params IStationery[] held)
    {
        var bag = new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('E'))).ToImmutableArray());
        var round = new RoundState(new RoundConfig(TargetScore: target, BoardSize: 5),
            BoardFromRows("CAT..", ".....", ".....", ".....", "....."), bag, HandOf("CATSORE"), Rng.FromSeed(5),
            Score: 0, SubmissionsLeft: submissionsLeft, DiscardsLeft: 3);
        return new GameSession(Config, RunState.New(1) with { Stationery = [.. held] }, RunPhase.InRound, round);
    }

    private static IReadOnlyList<RankedPlay> Ranked(GameSession session) =>
        MoveRanker.Rank(session.Round.Board, session.Round.Hand, Words, session.Run.DeskItems,
            session.Round.Config.EffectiveScoring(session.Scoring), session.Round.Config.MinWordLength);

    [Fact]
    public void LastSubmission_ShortOfTheTarget_UsesTheMarginClip()
    {
        var session = Round(target: 100_000, submissionsLeft: 1, new MarginClip());
        var ranked = Ranked(session);

        var (after, play) = StationeryBot.BeforePlay(session, ranked, ranked[^1], Words);

        Assert.Equal(2, after.Round.SubmissionsLeft);
        Assert.Empty(after.Run.Stationery);
        Assert.Null(play);
    }

    [Fact]
    public void AnswerKey_IsUsed_WhenOnlyTheBestPlayWinsTheRound()
    {
        var probe = Round(target: 1, submissionsLeft: 4);
        var ranked = Ranked(probe);
        var session = Round(target: ranked[0].Score.Total, submissionsLeft: 4, new AnswerKey());
        Assert.True(ranked[^1].Score.Total < ranked[0].Score.Total);

        var (after, play) = StationeryBot.BeforePlay(session, ranked, ranked[^1], Words);

        Assert.Empty(after.Run.Stationery);
        Assert.Equal(ranked[0].Score.Total, play!.Score.Total);
    }

    [Fact]
    public void NothingIsUsed_WhenTheChosenPlayAlreadyWins()
    {
        var session = Round(target: 1, submissionsLeft: 1, new MarginClip(), new AnswerKey());
        var ranked = Ranked(session);

        var (after, play) = StationeryBot.BeforePlay(session, ranked, ranked[^1], Words);

        Assert.Same(session, after);
        Assert.Null(play);
    }

    [Fact]
    public void Scissors_CutHardLetters_TheChosenPlayDoesNotUse()
    {
        var session = Round(target: 100_000, submissionsLeft: 4, new Scissors(MaxTiles: 2));
        session = session with { Round = session.Round with { Hand = HandOf("ATOQZXE") } };
        var ranked = Ranked(session);

        var (after, play) = StationeryBot.BeforePlay(session, ranked, ranked[0], Words);

        Assert.Empty(after.Run.Stationery);
        Assert.Null(play);
        Assert.Equal(2, after.Round.Hand.Tiles.Count(t => !session.Round.Hand.Contains(t.Id)));
    }

    [Fact]
    public void FountainPen_TurnsAnUnusedHardLetterWild_BeforeScissors()
    {
        var session = Round(target: 100_000, submissionsLeft: 4, new Scissors(), new FountainPen());
        session = session with { Round = session.Round with { Hand = HandOf("ATOQZXE") } };
        var ranked = Ranked(session);

        var (after, play) = StationeryBot.BeforePlay(session, ranked, ranked[0], Words);

        Assert.Null(play);
        Assert.IsType<Scissors>(Assert.Single(after.Run.Stationery));
        Assert.Equal(1, after.Round.Hand.Tiles.Count(t => t.IsWild));
        Assert.Contains(after.Round.Hand.Tiles, t => t.IsWild && t.Id is 3 or 4 or 5); // one of Q, Z, X
    }

    [Fact]
    public void Escape_CutsTheAwkwardTiles_WithScissors()
    {
        var session = Round(target: 1000, submissionsLeft: 4, new Scissors(MaxTiles: 2));
        session = session with
        {
            Round = session.Round with { Hand = HandOf("QZAEEEE"), DiscardsLeft = 0 },
        };

        var escaped = StationeryBot.Escape(session, Words);

        Assert.NotNull(escaped);
        Assert.Empty(escaped.Run.Stationery);
        Assert.DoesNotContain(escaped.Round.Hand.Tiles, t => t.Letter.Char is 'Q' or 'Z');
    }

    [Fact]
    public void Escape_WithNothingUseful_ReturnsNull()
    {
        var session = Round(target: 1000, submissionsLeft: 4, new MarginClip());

        Assert.Null(StationeryBot.Escape(session, Words));
    }
}
