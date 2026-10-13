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

    /// <summary>
    /// The same play with one tile (matched by id) replaced wherever it appears — placed tiles, formed words and the
    /// board after the play — so a shop bot can re-score a play as if a deck tile were enhanced. The play itself
    /// when the tile isn't in it.
    /// </summary>
    public PlayAnalysis WithTile(Tile tile)
    {
        int boardIndex = -1;
        for (int i = 0; i < BoardAfter.Cells.Length && boardIndex < 0; i++)
            if (BoardAfter.Cells[i]?.Id == tile.Id)
                boardIndex = i;
        bool inWords = Words.Any(w => w.Cells.Any(c => c.Tile.Id == tile.Id));
        if (boardIndex < 0 && !inWords && !Placed.Any(p => p.Tile.Id == tile.Id))
            return this;
        return new PlayAnalysis(
            Placed.Select(p => p.Tile.Id == tile.Id ? p with { Tile = tile } : p).ToImmutableArray(),
            Words.Select(w => w with { Cells = w.Cells.Select(c => c.Tile.Id == tile.Id ? c with { Tile = tile } : c).ToImmutableArray() }).ToImmutableArray(),
            boardIndex < 0 ? BoardAfter : BoardAfter with { Cells = BoardAfter.Cells.SetItem(boardIndex, tile) });
    }
}
