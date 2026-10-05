using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;

namespace Crossword.Core.Rules;

/// <summary>
/// Validates a set of tiles placed from the hand onto the board and extracts every word formed.
/// Rules: all new tiles in one row/column; the span between them filled (by new or existing tiles);
/// connected to existing tiles unless the board is empty; at least one 2+ letter word; every formed word at least
/// <c>minWordLength</c> letters (boss rule) and in the lexicon. Blocked cells cannot be played on.
/// </summary>
public static class PlacementValidator
{
    public static Result<PlayAnalysis, PlacementError> Validate(
        Board board, Hand hand, IReadOnlyList<PlacedTile> placed, ILexicon lexicon, int minWordLength = 2)
    {
        if (StructuralError(board, hand, placed) is { } error)
            return Result<PlayAnalysis, PlacementError>.Fail(error);

        var boardAfter = board.Place(placed);
        var newPositions = placed.Select(p => p.Position).ToHashSet();
        var words = ExtractWords(boardAfter, placed, newPositions);

        if (words.IsEmpty)
            return Result<PlayAnalysis, PlacementError>.Fail(new PlacementError.NoWordFormed());

        var tooShort = words.Where(w => w.Length < minWordLength).Select(w => w.Text).Distinct().ToImmutableArray();
        if (!tooShort.IsEmpty)
            return Result<PlayAnalysis, PlacementError>.Fail(new PlacementError.WordsTooShort(tooShort, minWordLength));

        var invalid = words.Select(w => w.Text).Where(t => !lexicon.Contains(t)).Distinct().ToImmutableArray();
        if (!invalid.IsEmpty)
            return Result<PlayAnalysis, PlacementError>.Fail(new PlacementError.InvalidWords(invalid));

        return Result<PlayAnalysis, PlacementError>.Ok(new PlayAnalysis(placed.ToImmutableArray(), words, boardAfter));
    }

    private static PlacementError? StructuralError(Board board, Hand hand, IReadOnlyList<PlacedTile> placed)
    {
        if (placed.Count == 0)
            return new PlacementError.NoTiles();

        var handIds = hand.Tiles.Select(t => t.Id).ToHashSet();
        var seenTiles = new HashSet<int>();
        var seenPositions = new HashSet<Position>();
        foreach (var (position, tile) in placed)
        {
            if (!handIds.Contains(tile.Id))
                return new PlacementError.TileNotInHand(tile);
            if (!seenTiles.Add(tile.Id))
                return new PlacementError.DuplicateTile(tile);
            if (!board.InBounds(position))
                return new PlacementError.OutOfBounds(position);
            if (board.IsBlocked(position))
                return new PlacementError.Blocked(position);
            if (!seenPositions.Add(position))
                return new PlacementError.DuplicatePosition(position);
            if (board.IsOccupied(position))
                return new PlacementError.Occupied(position);
        }

        bool sameRow = placed.All(p => p.Position.Row == placed[0].Position.Row);
        bool sameCol = placed.All(p => p.Position.Col == placed[0].Position.Col);
        if (!sameRow && !sameCol)
            return new PlacementError.NotInLine();

        var line = sameRow ? Direction.Across : Direction.Down;
        var ordered = placed.Select(p => p.Position).OrderBy(p => p.Row).ThenBy(p => p.Col).ToList();
        for (var p = ordered[0]; p != ordered[^1]; p = p.Step(line))
        {
            if (!seenPositions.Contains(p) && !board.IsOccupied(p))
                return new PlacementError.Gap(p);
        }

        bool connected = board.IsEmpty || placed.Any(p => p.Position.Neighbours().Any(board.IsOccupied));
        return connected ? null : new PlacementError.NotConnected();
    }

    /// <summary>Main word along the placement line first, then perpendicular cross words in placement order.</summary>
    private static ImmutableArray<FormedWord> ExtractWords(
        Board boardAfter, IReadOnlyList<PlacedTile> placed, HashSet<Position> newPositions)
    {
        var ordered = placed.Select(p => p.Position).OrderBy(p => p.Row).ThenBy(p => p.Col).ToList();
        var directions = ordered.Count == 1
            ? new[] { Direction.Across, Direction.Down }
            : new[] { ordered[0].Row == ordered[^1].Row ? Direction.Across : Direction.Down };

        var words = ImmutableArray.CreateBuilder<FormedWord>();
        var main = directions[0];

        if (WordThrough(boardAfter, ordered[0], main, newPositions) is { } mainWord)
            words.Add(mainWord);

        var cross = directions.Length == 2 ? Direction.Down : main.Perpendicular();
        foreach (var position in ordered)
        {
            if (WordThrough(boardAfter, position, cross, newPositions) is { } crossWord)
                words.Add(crossWord);
        }

        return words.ToImmutable();
    }

    /// <summary>The maximal run of tiles through <paramref name="through"/> in a direction, or null if under 2 letters.</summary>
    private static FormedWord? WordThrough(Board board, Position through, Direction direction, HashSet<Position> newPositions)
    {
        var start = through;
        while (board.IsOccupied(start.Step(direction, -1)))
            start = start.Step(direction, -1);

        var cells = ImmutableArray.CreateBuilder<WordCell>();
        for (var p = start; board.TileAt(p) is { } tile; p = p.Step(direction))
            cells.Add(new WordCell(p, tile, newPositions.Contains(p)));

        return cells.Count >= 2 ? new FormedWord(direction, cells.ToImmutable()) : null;
    }
}
