using System.Collections.Immutable;
using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>The undrawn tiles for the current round. Immutable; draws return a new bag.</summary>
public sealed record TileBag(ImmutableArray<Tile> Tiles)
{
    public static TileBag Empty { get; } = new(ImmutableArray<Tile>.Empty);

    public int Count => Tiles.Length;

    public bool IsEmpty => Tiles.IsEmpty;

    /// <summary>
    /// Draws up to <paramref name="count"/> tiles at random. If the bag holds fewer,
    /// all remaining tiles are drawn. Returns the drawn tiles, the remaining bag, and the advanced RNG.
    /// </summary>
    public (ImmutableArray<Tile> Drawn, TileBag Remaining, Rng Rng) Draw(int count, Rng rng)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot draw a negative number of tiles.");

        var remaining = Tiles.ToBuilder();
        var drawn = ImmutableArray.CreateBuilder<Tile>(Math.Min(count, remaining.Count));

        while (drawn.Count < count && remaining.Count > 0)
        {
            (int index, rng) = rng.NextInt(remaining.Count);
            drawn.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return (drawn.MoveToImmutable(), new TileBag(remaining.ToImmutable()), rng);
    }
}
