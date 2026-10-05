using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;

namespace Crossword.Tests.TestSupport;

internal static class Fixtures
{
    /// <summary>Small deterministic word list so tests don't depend on ENABLE's contents.</summary>
    public static readonly IWordGraph Words = Dawg.Build(
    [
        "CAT", "CATS", "CAR", "CARS", "AT", "TA", "AS", "SAT", "TO", "TOE", "ACT", "ACE", "ARC", "OAT",
        "TAR", "ARE", "EAR", "ERA", "AE", "RE", "ES", "ZA", "ZAX", "AX", "TE", "STAR",
    ]);

    /// <summary>Builds a board from rows of letters; '.' = empty. Board tiles get ids from 1000.</summary>
    public static Board BoardFromRows(params string[] rows)
    {
        var board = Board.Empty(rows.Length);
        int id = 1000;
        var placed = new List<PlacedTile>();
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] != '.')
                    placed.Add(new PlacedTile(new Position(r, c), new Tile(id++, Letter.From(rows[r][c]))));
            }
        }
        return board.Place(placed);
    }

    /// <summary>A hand whose tiles have ids 0..n-1 in letter order.</summary>
    public static Hand HandOf(string letters) =>
        new(letters.Select((c, i) => new Tile(i, Letter.From(c))).ToImmutableArray());

    /// <summary>
    /// Places <paramref name="word"/> starting at (row, col); letters over occupied cells are skipped,
    /// other letters are taken from <paramref name="hand"/>.
    /// </summary>
    public static List<PlacedTile> Spell(Board board, Hand hand, int row, int col, Direction dir, string word)
    {
        var available = hand.Tiles.ToList();
        var placed = new List<PlacedTile>();
        var pos = new Position(row, col);
        foreach (char ch in word)
        {
            if (!board.IsOccupied(pos))
            {
                var tile = available.First(t => t.Letter.Char == ch);
                available.Remove(tile);
                placed.Add(new PlacedTile(pos, tile));
            }
            pos = pos.Step(dir);
        }
        return placed;
    }

    /// <summary>Validates a play spelled with <see cref="Spell"/>; fails the test if it is illegal.</summary>
    public static PlayAnalysis PlayOn(Board board, string handLetters, int row, int col, Direction dir, string word)
    {
        var hand = HandOf(handLetters);
        var result = PlacementValidator.Validate(board, hand, Spell(board, hand, row, col, dir, word), Words);
        Xunit.Assert.True(result.IsOk, result.IsOk ? "" : result.Error.Message);
        return result.Value;
    }
}
