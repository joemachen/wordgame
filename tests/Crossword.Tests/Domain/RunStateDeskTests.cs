using Crossword.Core.DeskItems;
using Crossword.Core.Domain;

namespace Crossword.Tests.Domain;

public class RunStateDeskTests
{
    private static RunState With(params Crossword.Core.Effects.IDeskItem[] items) =>
        items.Aggregate(RunState.New(1), (run, item) => run.AddDeskItem(item).Value);

    [Fact]
    public void AddDeskItem_AppendsToRightmostSlot_WithoutMutatingOriginal()
    {
        var run = RunState.New(1);

        var next = run.AddDeskItem(new RedPen()).Value;

        Assert.Empty(run.DeskItems);
        Assert.IsType<RedPen>(Assert.Single(next.DeskItems));
    }

    [Fact]
    public void AddDeskItem_RejectsDuplicates()
    {
        Assert.False(With(new RedPen()).AddDeskItem(new RedPen(Mult: 10)).IsOk);
    }

    [Fact]
    public void AddDeskItem_RejectsWhenFull()
    {
        var full = With(DeskItemCatalog.All.Take(RunState.MaxDeskSlots).ToArray());

        Assert.False(full.AddDeskItem(DeskItemCatalog.All[RunState.MaxDeskSlots]).IsOk);
    }

    [Fact]
    public void RemoveDeskItem_RemovesSlot_AndRejectsEmptySlot()
    {
        var run = With(new RedPen(), new Thesaurus());

        Assert.IsType<Thesaurus>(Assert.Single(run.RemoveDeskItem(0).Value.DeskItems));
        Assert.False(run.RemoveDeskItem(2).IsOk);
    }

    [Fact]
    public void MoveDeskItem_ReordersSlots()
    {
        var run = With(new RedPen(), new Thesaurus(), new Inkwell());

        var moved = run.MoveDeskItem(0, 2).Value;

        Assert.Equal(["thesaurus", "inkwell", "red-pen"], moved.DeskItems.Select(i => i.Id));
        Assert.False(run.MoveDeskItem(0, 3).IsOk);
    }
}
