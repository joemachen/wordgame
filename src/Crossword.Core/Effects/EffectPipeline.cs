namespace Crossword.Core.Effects;

public static class EffectPipeline
{
    /// <summary>Applies Desk Items strictly in slot order (left to right).</summary>
    public static ScoreContext Apply(IEnumerable<IDeskItem> itemsInSlotOrder, ScoreContext context) =>
        itemsInSlotOrder.Aggregate(context, (ctx, item) => item.Apply(ctx));
}
