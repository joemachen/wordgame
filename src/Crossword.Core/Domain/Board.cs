using System.Collections.Immutable;

namespace Crossword.Core.Domain;

public sealed record PlacedTile(Position Position, Tile Tile);

/// <summary>
/// Square crossword grid for one round. Cells and premiums are stored row-major.
/// <see cref="Blocked"/> cells (crossword "black squares") can never hold tiles and end words like the board edge.
/// </summary>
public sealed record Board(int Size, ImmutableArray<Tile?> Cells, ImmutableArray<Premium> Premiums)
{
    public ImmutableHashSet<Position> Blocked { get; init; } = ImmutableHashSet<Position>.Empty;

    public static Board Empty(int size, ImmutableArray<Premium>? premiums = null)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Board size must be positive.");

        var layout = premiums ?? ImmutableArray.CreateRange(Enumerable.Repeat(Premium.None, size * size));
        if (layout.Length != size * size)
            throw new ArgumentException($"Premium layout must have {size * size} cells.", nameof(premiums));

        return new Board(size, ImmutableArray.CreateRange(Enumerable.Repeat<Tile?>(null, size * size)), layout);
    }

    public bool IsEmpty => Cells.All(c => c is null);

    public bool InBounds(Position p) => p.Row >= 0 && p.Row < Size && p.Col >= 0 && p.Col < Size;

    public Tile? TileAt(Position p) => InBounds(p) ? Cells[Index(p)] : null;

    public bool IsOccupied(Position p) => TileAt(p) is not null;

    public bool IsBlocked(Position p) => Blocked.Contains(p);

    public Premium PremiumAt(Position p) => Premiums[Index(p)];

    public Board Place(IEnumerable<PlacedTile> tiles)
    {
        var cells = Cells.ToBuilder();
        foreach (var placed in tiles)
        {
            if (!InBounds(placed.Position))
                throw new ArgumentOutOfRangeException(nameof(tiles), placed.Position, "Position is off the board.");
            if (IsBlocked(placed.Position))
                throw new InvalidOperationException($"Cell {placed.Position} is blocked.");
            if (cells[Index(placed.Position)] is not null)
                throw new InvalidOperationException($"Cell {placed.Position} is already occupied.");
            cells[Index(placed.Position)] = placed.Tile;
        }
        return this with { Cells = cells.MoveToImmutable() };
    }

    private int Index(Position p) => p.Row * Size + p.Col;
}
