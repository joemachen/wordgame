using Crossword.Core.Effects;

namespace Crossword.Tests.Effects;

[Trait("Category", "Scoring")]
public class EffectPipelineTests
{
    private sealed record AddBase(string Id, long Amount) : IDeskItem
    {
        public ScoreContext Apply(ScoreContext c) =>
            (c with { Base = c.Base + Amount }).Record(new EffectEvent(Id, $"+{Amount} base"));
    }

    private sealed record TimesMult(string Id, long Factor) : IDeskItem
    {
        public ScoreContext Apply(ScoreContext c) =>
            (c with { Multiplier = c.Multiplier * Factor }).Record(new EffectEvent(Id, $"x{Factor} mult"));
    }

    [Fact]
    public void Apply_WithNoItems_ReturnsContextUnchanged()
    {
        var start = ScoreContext.Start(10);

        Assert.Same(start, EffectPipeline.Apply([], start));
    }

    [Fact]
    public void Apply_RunsItemsInSlotOrder_AndLogsEachEffect()
    {
        IDeskItem[] items = [new AddBase("a", 5), new TimesMult("b", 3), new AddBase("c", 1)];

        var result = EffectPipeline.Apply(items, ScoreContext.Start(10));

        Assert.Equal(16, result.Base);
        Assert.Equal(3, result.Multiplier);
        Assert.Equal(48, result.Total);
        Assert.Equal(["a", "b", "c"], result.Log.Select(e => e.SourceId));
    }

    [Fact]
    public void Apply_DoesNotMutateStartingContext()
    {
        var start = ScoreContext.Start(10);

        EffectPipeline.Apply([new AddBase("a", 5)], start);

        Assert.Equal(10, start.Base);
        Assert.Empty(start.Log);
    }
}
