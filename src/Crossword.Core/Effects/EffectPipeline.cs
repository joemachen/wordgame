namespace Crossword.Core.Effects;

public static class EffectPipeline
{
    /// <summary>Applies Desk Items strictly in slot order (left to right).</summary>
    public static ScoreContext Apply(IEnumerable<IDeskItem> itemsInSlotOrder, ScoreContext context)
    {
        var desk = itemsInSlotOrder as IReadOnlyList<IDeskItem> ?? itemsInSlotOrder.ToList();
        return desk.Aggregate(context, (ctx, item) => item.Apply(ctx, desk));
    }
}
