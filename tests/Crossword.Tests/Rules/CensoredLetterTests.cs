using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Rules;

/// <summary>Censored Press: one letter per round can't be placed, not even by a wild tile.</summary>
public class CensoredLetterTests
{
    [Fact]
    public void Validator_RefusesTheCensoredLetter()
    {
        var board = Board.Empty(5);
        var hand = HandOf("CAT");
        var cat = Spell(board, hand, 0, 0, Direction.Across, "CAT");

        var refused = PlacementValidator.Validate(board, hand, cat, Words, censoredLetter: 'T');

        Assert.Equal('T', Assert.IsType<PlacementError.Censored>(refused.Error).Letter);
        Assert.True(PlacementValidator.Validate(board, hand, cat, Words, censoredLetter: 'S').IsOk);
    }

    [Fact]
    public void Validator_RefusesAWildPlayedAsTheCensoredLetter()
    {
        var board = Board.Empty(5);
        var wild = Tile.Wild(9);
        var hand = new Hand([.. HandOf("CA").Tiles, wild]);
        PlacedTile[] cat =
        [
            new(new Position(0, 0), hand.Tiles[0]),
            new(new Position(0, 1), hand.Tiles[1]),
            new(new Position(0, 2), wild.As(Letter.From('T'))),
        ];

        Assert.IsType<PlacementError.Censored>(PlacementValidator.Validate(board, hand, cat, Words, censoredLetter: 'T').Error);
        Assert.True(PlacementValidator.Validate(board, hand, cat, Words).IsOk);
    }

    [Fact]
    public void MoveGenerator_NeverPlacesTheCensoredLetter_NotEvenAsAWild()
    {
        var board = Board.Empty(5);
        var hand = new Hand([.. HandOf("CATSORE").Tiles, Tile.Wild(9)]);

        var plays = MoveGenerator.LegalPlays(board, hand, Words, censoredLetter: 'T').ToList();

        Assert.NotEmpty(plays); // e.g. CAR, EAR, ARC
        Assert.DoesNotContain(plays, play => play.Placed.Any(p => p.Tile.Letter.Char == 'T'));
        Assert.Contains(MoveGenerator.LegalPlays(board, hand, Words), play => play.Placed.Any(p => p.Tile.Letter.Char == 'T'));
    }

    [Fact]
    public void AHandThatCanOnlyPlayTheCensoredLetter_Deadlocks()
    {
        RoundState Round(char? censored) => new(
            new RoundConfig(TargetScore: 1000, BoardSize: 5, CensoredLetter: censored),
            Board.Empty(5), TileBag.Empty, HandOf("AT"), Rng.FromSeed(5), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 0);

        Assert.False(RoundRules.CheckDeadlock(Round(null), Words).Deadlocked); // AT / TA
        Assert.True(RoundRules.CheckDeadlock(Round('T'), Words).Deadlocked);
    }

    [Fact]
    public void Submit_RefusesTheCensoredLetter()
    {
        var round = new RoundState(
            new RoundConfig(TargetScore: 1000, BoardSize: 5, CensoredLetter: 'C'),
            Board.Empty(5), TileBag.Empty, HandOf("CATSORE"), Rng.FromSeed(5), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);

        var result = RoundRules.Submit(round, Spell(round.Board, round.Hand, 0, 0, Direction.Across, "CAT"), Words, [],
            Crossword.Core.Scoring.ScoringConfig.Default);

        var error = Assert.IsType<RoundError.InvalidPlacement>(result.Error);
        Assert.IsType<PlacementError.Censored>(error.Error);
    }

    [Fact]
    [Trait("Category", "Determinism")]
    public void TheCensoredLetter_ComesFromSeedAndRound_AndLeavesTheDrawsUnchanged()
    {
        var censoring = RunConfig.Default with { CensoredLetters = "DLNRST" };

        var plain = RunRules.NewGame(11, RunConfig.Default, LexiconLoader.Enable);
        var censored = RunRules.NewGame(11, censoring, LexiconLoader.Enable);

        Assert.Null(plain.Round.Config.CensoredLetter);
        Assert.Contains(censored.Round.Config.CensoredLetter!.Value, "DLNRST");
        Assert.Equal(plain.Round.Hand.Tiles.Select(t => t.Id), censored.Round.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(plain.Round.Rng, censored.Round.Rng);
        Assert.Equal(plain.Run.Rng, censored.Run.Rng);

        var run = censored.Run;
        Assert.Equal(censored.Round.Config.CensoredLetter, RunRules.CensoredLetterFor(censoring, run, 0));
        var letters = Enumerable.Range(0, 15).Select(round => RunRules.CensoredLetterFor(censoring, run, round)).ToList();
        Assert.Equal(letters, Enumerable.Range(0, 15).Select(round => RunRules.CensoredLetterFor(censoring, run, round)));
        Assert.True(letters.Distinct().Count() > 1);
    }
}
