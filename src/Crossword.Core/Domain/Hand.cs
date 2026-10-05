using System.Collections.Immutable;

namespace Crossword.Core.Domain;

/// <summary>The tiles currently available to the player.</summary>
public sealed record Hand(ImmutableArray<Tile> Tiles)
{
    public static Hand Empty { get; } = new(ImmutableArray<Tile>.Empty);

    public int Count => Tiles.Length;

    public bool Contains(int tileId) => Tiles.Any(t => t.Id == tileId);

    public Hand Add(IEnumerable<Tile> tiles) => new(Tiles.AddRange(tiles));

    public Hand Remove(IEnumerable<int> tileIds)
    {
        var ids = tileIds.ToHashSet();
        return new(Tiles.RemoveAll(t => ids.Contains(t.Id)));
    }

    public override string ToString() => string.Join(' ', Tiles);
}
