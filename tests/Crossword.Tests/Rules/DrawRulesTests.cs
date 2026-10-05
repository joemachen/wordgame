using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Rules;

namespace Crossword.Tests.Rules;

public class DrawRulesTests
{
    private static RoundState EmptyHandRound(int handSize = 7, int deckSize = 98) => new(
        Config: new RoundConfig(TargetScore: 100, HandSize: handSize),
        Board: Board.Empty(7),
        Bag: new TileBag(StartingDeck.Create().Take(deckSize).ToImmutableArray()),
        Hand: Hand.Empty,
        Rng: Rng.FromSeed(1),
        Score: 0,
        SubmissionsLeft: 4,
        DiscardsLeft: 3);

    [Fact]
    public void DrawToHandSize_FillsHand_AndShrinksBag()
    {
        var round = EmptyHandRound();

        var next = DrawRules.DrawToHandSize(round);

        Assert.Equal(7, next.Hand.Count);
        Assert.Equal(round.Bag.Count - 7, next.Bag.Count);
    }

    [Fact]
    public void DrawToHandSize_ReturnsNewState_LeavingOriginalUntouched()
    {
        var round = EmptyHandRound();

        var next = DrawRules.DrawToHandSize(round);

        Assert.NotSame(round, next);
        Assert.Equal(0, round.Hand.Count);
        Assert.NotEqual(round.Rng, next.Rng);
    }

    [Fact]
    public void DrawToHandSize_WhenHandFull_ReturnsSameState()
    {
        var full = DrawRules.DrawToHandSize(EmptyHandRound());

        Assert.Same(full, DrawRules.DrawToHandSize(full));
    }

    [Fact]
    public void DrawToHandSize_IsReproducible()
    {
        var a = DrawRules.DrawToHandSize(EmptyHandRound());
        var b = DrawRules.DrawToHandSize(EmptyHandRound());

        Assert.Equal(a.Hand.Tiles.Select(t => t.Id), b.Hand.Tiles.Select(t => t.Id));
    }

    [Fact]
    public void DrawToHandSize_WithNearlyEmptyBag_DrawsWhatIsLeft()
    {
        var next = DrawRules.DrawToHandSize(EmptyHandRound(deckSize: 3));

        Assert.Equal(3, next.Hand.Count);
        Assert.True(next.Bag.IsEmpty);
    }
}
