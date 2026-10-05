using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Rules;

public sealed record WordCell(Position Position, Tile Tile, bool IsNew);

/// <summary>A word formed (created or extended) by the current play.</summary>
public sealed record FormedWord(Direction Direction, ImmutableArray<WordCell> Cells)
{
    public string Text => string.Concat(Cells.Select(c => c.Tile.Letter.Char));

    public int Length => Cells.Length;

    public override string ToString() => $"{Text} ({Cells[0].Position} {Direction})";
}

/// <summary>A validated play: the tiles placed, every word formed, and the board afterwards.</summary>
public sealed record PlayAnalysis(ImmutableArray<PlacedTile> Placed, ImmutableArray<FormedWord> Words, Board BoardAfter)
{
    /// <summary>Newly placed tiles that sit in both an Across and a Down word formed this play.</summary>
    public ImmutableArray<Position> Intersections =>
        Placed.Select(p => p.Position)
            .Where(pos =>
                Words.Any(w => w.Direction == Direction.Across && w.Cells.Any(c => c.Position == pos)) &&
                Words.Any(w => w.Direction == Direction.Down && w.Cells.Any(c => c.Position == pos)))
            .ToImmutableArray();

    public FormedWord LongestWord => Words.MaxBy(w => w.Length)!;
}
