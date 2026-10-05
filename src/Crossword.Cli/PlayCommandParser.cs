using Crossword.Core.Domain;

namespace Crossword.Cli;

public sealed record PlayCommand(Position Start, Direction Direction, string Word);

/// <summary>
/// Turns typed commands into Core inputs. Players type the whole word (including letters already on
/// the board) from a start cell; letters over occupied cells must match and are not taken from the hand.
/// </summary>
public static class PlayCommandParser
{
    /// <summary>Parses "C4", "c4" → column C, row 4 (zero-based (3, 2)).</summary>
    public static Result<Position, string> ParseCell(string text)
    {
        text = text.Trim().ToUpperInvariant();
        if (text.Length < 2 || text[0] is < 'A' or > 'Z' || !int.TryParse(text[1..], out int row) || row < 1)
            return Result<Position, string>.Fail($"'{text}' is not a cell like C4.");
        return Result<Position, string>.Ok(new Position(row - 1, text[0] - 'A'));
    }

    /// <summary>Parses the arguments of "play &lt;cell&gt; &lt;a|d&gt; &lt;word&gt;".</summary>
    public static Result<PlayCommand, string> ParsePlay(IReadOnlyList<string> args)
    {
        if (args.Count != 3)
            return Result<PlayCommand, string>.Fail("Usage: play <cell> <a|d> <WORD>   e.g. play C4 a CRANE");

        var cell = ParseCell(args[0]);
        if (!cell.IsOk)
            return Result<PlayCommand, string>.Fail(cell.Error);

        Direction? direction = args[1].ToLowerInvariant() switch
        {
            "a" or "across" or "h" => Direction.Across,
            "d" or "down" or "v" => Direction.Down,
            _ => null,
        };
        if (direction is null)
            return Result<PlayCommand, string>.Fail($"Direction must be 'a' (across) or 'd' (down), not '{args[1]}'.");

        string word = args[2].ToUpperInvariant();
        if (word.Length == 0 || word.Any(c => c is < 'A' or > 'Z'))
            return Result<PlayCommand, string>.Fail($"'{args[2]}' must be letters A-Z only.");

        return Result<PlayCommand, string>.Ok(new PlayCommand(cell.Value, direction.Value, word));
    }

    /// <summary>Maps a play command onto concrete hand tiles placed on empty cells.</summary>
    public static Result<IReadOnlyList<PlacedTile>, string> ResolveTiles(Board board, Hand hand, PlayCommand command)
    {
        var available = hand.Tiles.ToList();
        var placed = new List<PlacedTile>();
        var position = command.Start;

        foreach (char letter in command.Word)
        {
            if (!board.InBounds(position))
                return Result<IReadOnlyList<PlacedTile>, string>.Fail($"{command.Word} runs off the board at {position}.");

            if (board.TileAt(position) is { } existing)
            {
                if (existing.Letter.Char != letter)
                    return Result<IReadOnlyList<PlacedTile>, string>.Fail($"{position} already has {existing}, not {letter}.");
            }
            else
            {
                var tile = available.FirstOrDefault(t => t.Letter.Char == letter);
                if (tile is null)
                    return Result<IReadOnlyList<PlacedTile>, string>.Fail($"No '{letter}' left in your hand.");
                available.Remove(tile);
                placed.Add(new PlacedTile(position, tile));
            }

            position = position.Step(command.Direction);
        }

        return placed.Count == 0
            ? Result<IReadOnlyList<PlacedTile>, string>.Fail("That word uses no tiles from your hand.")
            : Result<IReadOnlyList<PlacedTile>, string>.Ok(placed);
    }

    /// <summary>
    /// Selects deck tiles by letters for shop edits, e.g. "QV". Plain tiles are preferred so an enhanced tile
    /// is only picked when it's the only one of that letter left.
    /// </summary>
    public static Result<IReadOnlyList<int>, string> ResolveDeckTiles(IReadOnlyList<Tile> deck, string letters)
    {
        var available = deck.OrderBy(t => t.Enhancement == TileEnhancement.None ? 0 : 1).ToList();
        var ids = new List<int>();
        foreach (char raw in letters.ToUpperInvariant())
        {
            var tile = available.FirstOrDefault(t => t.Letter.Char == raw);
            if (tile is null)
                return Result<IReadOnlyList<int>, string>.Fail($"No '{raw}' tile left in your deck.");
            available.Remove(tile);
            ids.Add(tile.Id);
        }
        return Result<IReadOnlyList<int>, string>.Ok(ids);
    }

    /// <summary>Selects hand tiles by letters, e.g. "QXE" (repeat a letter to pick duplicates).</summary>
    public static Result<IReadOnlyList<int>, string> ResolveDiscard(Hand hand, string letters)
    {
        var available = hand.Tiles.ToList();
        var ids = new List<int>();
        foreach (char raw in letters.ToUpperInvariant())
        {
            var tile = available.FirstOrDefault(t => t.Letter.Char == raw);
            if (tile is null)
                return Result<IReadOnlyList<int>, string>.Fail($"No '{raw}' left in your hand to discard.");
            available.Remove(tile);
            ids.Add(tile.Id);
        }
        return Result<IReadOnlyList<int>, string>.Ok(ids);
    }
}
