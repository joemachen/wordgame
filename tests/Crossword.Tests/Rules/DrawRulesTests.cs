using Crossword.Core.Domain;
using Crossword.Core.Rules;

namespace Crossword.Tests.Rules;

public class DrawRulesTests
{
    [Fact]
    public void NewRun_StartsWithEmptyHandAndFullBag()
    {
        var run = RunState.New(seed: 1);

        Assert.Equal(0, run.Hand.Count);
        Assert.Equal(StartingDeck.Create().Length, run.Bag.Count);
    }

    [Fact]
    public void DrawToHandSize_FillsHand_AndShrinksBag()
    {
        var run = RunState.New(seed: 1);

        var next = DrawRules.DrawToHandSize(run);

        Assert.Equal(run.HandSize, next.Hand.Count);
        Assert.Equal(run.Bag.Count - run.HandSize, next.Bag.Count);
    }

    [Fact]
    public void DrawToHandSize_ReturnsNewState_LeavingOriginalUntouched()
    {
        var run = RunState.New(seed: 1);

        var next = DrawRules.DrawToHandSize(run);

        Assert.NotSame(run, next);
        Assert.Equal(0, run.Hand.Count);
        Assert.NotEqual(run.Rng, next.Rng);
    }

    [Fact]
    public void DrawToHandSize_WhenHandFull_ReturnsSameState()
    {
        var full = DrawRules.DrawToHandSize(RunState.New(seed: 1));

        Assert.Same(full, DrawRules.DrawToHandSize(full));
    }

    [Fact]
    public void DrawToHandSize_IsReproducibleFromSeed()
    {
        var a = DrawRules.DrawToHandSize(RunState.New(seed: 2026));
        var b = DrawRules.DrawToHandSize(RunState.New(seed: 2026));

        Assert.Equal(a.Hand.Tiles.Select(t => t.Id), b.Hand.Tiles.Select(t => t.Id));
    }

    [Fact]
    public void DrawToHandSize_WithNearlyEmptyBag_DrawsWhatIsLeft()
    {
        var run = RunState.New(seed: 1, handSize: 200);

        var next = DrawRules.DrawToHandSize(run);

        Assert.Equal(run.Bag.Count, next.Hand.Count);
        Assert.True(next.Bag.IsEmpty);
    }
}
