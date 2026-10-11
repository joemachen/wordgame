using System.Collections.Immutable;
using System.Text;
using Crossword.Core.Domain;

namespace Crossword.Core.Clues;

/// <summary>A word on the board with its crossword clue number.</summary>
public sealed record NumberedWord(int Number, Direction Direction, Position Start, string Text);

/// <summary>Reads the words on a board the way a printed crossword numbers them.</summary>
public static class BoardWords
{
    /// <summary>
    /// Every maximal run of 2+ tiles across and down. Numbers follow crossword convention: cells are numbered in
    /// reading order (row by row), and a cell that starts both an across and a down word gets a single number.
    /// </summary>
    public static ImmutableArray<NumberedWord> Numbered(Board board)
    {
        var words = ImmutableArray.CreateBuilder<NumberedWord>();
        int number = 0;
        for (int row = 0; row < board.Size; row++)
        {
            for (int col = 0; col < board.Size; col++)
            {
                var cell = new Position(row, col);
                if (!board.IsOccupied(cell))
                    continue;
                bool across = Starts(board, cell, Direction.Across);
                bool down = Starts(board, cell, Direction.Down);
                if (!across && !down)
                    continue;
                number++;
                if (across)
                    words.Add(new NumberedWord(number, Direction.Across, cell, Read(board, cell, Direction.Across)));
                if (down)
                    words.Add(new NumberedWord(number, Direction.Down, cell, Read(board, cell, Direction.Down)));
            }
        }
        return words.ToImmutable();
    }

    /// <summary>The words (across, then down) running through <paramref name="cell"/>; none when it is empty or alone.</summary>
    public static ImmutableArray<NumberedWord> Through(Board board, Position cell) =>
        Numbered(board).Where(w => Covers(board, w, cell)).ToImmutableArray();

    private static bool Covers(Board board, NumberedWord word, Position cell)
    {
        for (var p = word.Start; board.TileAt(p) is not null; p = p.Step(word.Direction))
        {
            if (p == cell)
                return true;
        }
        return false;
    }

    private static bool Starts(Board board, Position cell, Direction direction) =>
        !board.IsOccupied(cell.Step(direction, -1)) && board.IsOccupied(cell.Step(direction));

    private static string Read(Board board, Position start, Direction direction)
    {
        var text = new StringBuilder();
        for (var p = start; board.TileAt(p) is { } tile; p = p.Step(direction))
            text.Append(tile.Letter.Char);
        return text.ToString();
    }
}
