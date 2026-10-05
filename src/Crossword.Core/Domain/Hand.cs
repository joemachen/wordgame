using System.Collections.Immutable;

namespace Crossword.Core.Domain;

/// <summary>The tiles currently available to the player.</summary>
public sealed record Hand(ImmutableArray<Tile> Tiles)
{
    public static Hand Empty { get; } = new(ImmutableArray<Tile>.Empty);

    public int Count => Tiles.Length;

    public Hand Add(IEnumerable<Tile> tiles) => new(Tiles.AddRange(tiles));

    public override string ToString() => string.Join(' ', Tiles);
}
