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
    public void Highlighter_MarksTheMostValuableLetterTheChosenPlayPlaces_WhenWorthIt()
    {
        var cheap = Round(target: 100_000, submissionsLeft: 4, new Highlighter(3));
        var cheapRanked = Ranked(cheap);
        var (unchanged, _) = StationeryBot.BeforePlay(cheap, cheapRanked, cheapRanked[0], Words);
        Assert.Null(unchanged.Round.Config.Highlight); // C A T S O R E: nothing worth 4
        Assert.Single(unchanged.Run.Stationery);

        // With T worth 8, a play placing the T (tile 2) is worth highlighting.
        var rich = cheap with { Config = Config with { Scoring = ScoringConfig.Default with { LetterValues = ScoringConfig.Default.LetterValues.SetItem('T', 8) } } };
        var ranked = Ranked(rich);
        var choice = ranked.First(r => r.Play.Placed.Length == 1 && r.Play.Placed[0].Tile.Letter.Char == 'T');
        var (after, play) = StationeryBot.BeforePlay(rich, ranked, choice, Words);

        Assert.Equal(new TileHighlight(2, 3), after.Round.Config.Highlight);
        Assert.Empty(after.Run.Stationery);
        Assert.Null(play);
    }

    [Fact]
    public void Clipping_IsUsedOnTheLastSubmission_WhenShort_OnTheLongestBoardWord()
    {
        var early = Round(target: 100_000, submissionsLeft: 4, new Clipping());
        var earlyRanked = Ranked(early);
        Assert.Null(StationeryBot.BeforePlay(early, earlyRanked, earlyRanked[^1], Words).Session.Round.Config.Clipping);

        var last = Round(target: 100_000, submissionsLeft: 1, new Clipping());
        var ranked = Ranked(last);
        var (after, _) = StationeryBot.BeforePlay(last, ranked, ranked[^1], Words);

        Assert.Equal(new ClippedWord(new Position(0, 0), Direction.Across, "CAT"), after.Round.Config.Clipping);
        Assert.Empty(after.Run.Stationery);
    }

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
