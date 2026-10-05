using System.Collections.Immutable;
using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>
/// Display order of hand tiles (by tile id). Purely cosmetic — no rule depends on it — but kept as pure,
/// tested helpers so every front-end arranges hands the same way.
/// </summary>
public static class HandArrangement
{
    /// <summary>
    /// Keeps the player's order for tiles still in the hand and appends newly drawn tiles in draw order.
    /// </summary>
    public static ImmutableArray<int> Reconcile(IReadOnlyList<int> order, Hand hand)
    {
        var inHand = hand.Tiles.Select(t => t.Id).ToHashSet();
        var kept = order.Where(inHand.Contains).Distinct().ToList();
        var known = kept.ToHashSet();
        kept.AddRange(hand.Tiles.Select(t => t.Id).Where(id => !known.Contains(id)));
        return kept.ToImmutableArray();
    }

    /// <summary>Moves <paramref name="tileId"/> next to <paramref name="targetId"/> (before it, or after it).</summary>
    public static ImmutableArray<int> Move(IReadOnlyList<int> order, int tileId, int targetId, bool after)
    {
        if (tileId == targetId || !order.Contains(tileId) || !order.Contains(targetId))
            return order.ToImmutableArray();

        var list = order.Where(id => id != tileId).ToList();
        int index = list.IndexOf(targetId) + (after ? 1 : 0);
        list.Insert(index, tileId);
        return list.ToImmutableArray();
    }

    /// <summary>
    /// Shuffles the order. With two or more tiles the result always differs from the input, so a Shuffle
    /// button never appears to do nothing (ignoring duplicate letters, which share no ids).
    /// </summary>
    public static (ImmutableArray<int> Order, Rng Rng) Shuffle(IReadOnlyList<int> order, Rng rng)
    {
        if (order.Count < 2)
            return (order.ToImmutableArray(), rng);

        ImmutableArray<int> shuffled;
        do
        {
            (shuffled, rng) = rng.Shuffle(order);
        }
        while (shuffled.SequenceEqual(order));
        return (shuffled, rng);
    }
}
