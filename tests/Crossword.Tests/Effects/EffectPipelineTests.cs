using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Effects;

[Trait("Category", "Scoring")]
public class EffectPipelineTests
{
    internal sealed record PlusMult(string Id, decimal Amount) : IDeskItem
    {
        public string Name => $"+{Amount} Mult";
        public ScoreContext Apply(ScoreContext c) => c.AddMult(Amount).Record(Id, Name);
    }

    internal sealed record TimesMult(string Id, decimal Factor) : IDeskItem
    {
        public string Name => $"x{Factor} Mult";
        public ScoreContext Apply(ScoreContext c) => c.TimesMult(Factor).Record(Id, Name);
    }

    private static readonly PlayAnalysis AnyPlay = MakePlay();

    private static PlayAnalysis MakePlay()
    {
        var board = Board.Empty(5);
        var hand = HandOf("CAT");
        return PlacementValidator.Validate(board, hand, Spell(board, hand, 0, 0, Direction.Across, "CAT"), Words).Value;
    }

    [Fact]
    public void Apply_WithNoItems_ReturnsContextUnchanged()
    {
        var start = ScoreContext.Start(AnyPlay, 10, 1);

        Assert.Same(start, EffectPipeline.Apply([], start));
    }

    [Fact]
    public void Apply_RunsItemsInSlotOrder_AndLogsEachEffect()
    {
        IDeskItem[] items = [new PlusMult("a", 2), new TimesMult("b", 3), new PlusMult("c", 1)];

        var result = EffectPipeline.Apply(items, ScoreContext.Start(AnyPlay, 10, 1));

        Assert.Equal(10m, result.Mult); // ((1 + 2) × 3) + 1
        Assert.Equal(100, result.Total);
        Assert.Equal(["a", "b", "c"], result.Log.Select(e => e.SourceId));
        Assert.Equal([3m, 9m, 10m], result.Log.Select(e => e.MultAfter));
    }

    [Fact]
    public void SlotOrder_ChangesOutcome()
    {
        var start = ScoreContext.Start(AnyPlay, 10, 1);

        var addFirst = EffectPipeline.Apply([new PlusMult("a", 2), new TimesMult("b", 3)], start);
        var timesFirst = EffectPipeline.Apply([new TimesMult("b", 3), new PlusMult("a", 2)], start);

        Assert.Equal(9m, addFirst.Mult);
        Assert.Equal(5m, timesFirst.Mult);
    }

    [Fact]
    public void Apply_DoesNotMutateStartingContext()
    {
        var start = ScoreContext.Start(AnyPlay, 10, 1);

        EffectPipeline.Apply([new PlusMult("a", 5)], start);

        Assert.Equal(1m, start.Mult);
        Assert.Empty(start.Log);
    }

    [Fact]
    public void Total_FloorsFractionalMult()
    {
        var context = ScoreContext.Start(AnyPlay, 7, 1).TimesMult(1.5m);

        Assert.Equal(10, context.Total); // 10.5 → 10
    }
}
