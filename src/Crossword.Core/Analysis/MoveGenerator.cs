using System.Collections.Immutable;
using System.Text;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;

namespace Crossword.Core.Analysis;

/// <summary>
/// Enumerates every legal play for a board and hand. For each line and start cell it walks the word
/// graph letter by letter — consuming board letters and trying hand letters on empty cells — pruning
/// dead prefixes and invalid cross words immediately. Results are re-checked by <see cref="PlacementValidator"/>.
/// </summary>
public static class MoveGenerator
{
    public static IEnumerable<PlayAnalysis> LegalPlays(Board board, Hand hand, IWordGraph lexicon, int minWordLength = 2)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in Candidates(board, hand, lexicon, minWordLength))
        {
            var placed = AssignTiles(hand, candidate);
            if (!seen.Add(Key(placed)))
                continue; // single-tile plays are found once per direction

            var result = PlacementValidator.Validate(board, hand, placed, lexicon, minWordLength);
            if (result.IsOk)
                yield return result.Value;
        }
    }

    public static bool HasLegalPlay(Board board, Hand hand, IWordGraph lexicon, int minWordLength = 2) =>
        LegalPlays(board, hand, lexicon, minWordLength).Any();

    private static IEnumerable<IReadOnlyList<(Position Position, char Letter)>> Candidates(
        Board board, Hand hand, IWordGraph lexicon, int minWordLength)
    {
        var counts = new int[26];
        foreach (var tile in hand.Tiles)
            counts[tile.Letter.Char - 'A']++;

        var search = new Search(board, lexicon, counts, board.IsEmpty, minWordLength);
        foreach (var direction in new[] { Direction.Across, Direction.Down })
        {
            for (int row = 0; row < board.Size; row++)
            {
                for (int col = 0; col < board.Size; col++)
                {
                    var start = new Position(row, col);
                    if (board.IsOccupied(start.Step(direction, -1)))
                        continue; // a word can't start right after a board letter
                    foreach (var move in search.From(start, direction))
                        yield return move;
                }
            }
        }
    }

    private sealed class Search(Board board, IWordGraph lexicon, int[] counts, bool boardEmpty, int minWordLength)
    {
        private readonly List<(Position, char)> _placed = new();

        public List<IReadOnlyList<(Position, char)>> From(Position start, Direction direction)
        {
            var found = new List<IReadOnlyList<(Position, char)>>();
            Extend(start, direction, lexicon.Root, length: 0, touches: false, found);
            return found;
        }

        private void Extend(Position pos, Direction dir, int node, int length, bool touches,
            List<IReadOnlyList<(Position, char)>> found)
        {
            if (length > WordNormalizer.MaxLength)
                return;

            if (board.TileAt(pos) is { } existing)
            {
                int next = lexicon.Step(node, existing.Letter.Char);
                if (next != IWordGraph.NoNode)
                    Extend(pos.Step(dir), dir, next, length + 1, touches: true, found);
                return;
            }

            // pos is empty, blocked or off-board: the word may end here.
            if (length >= minWordLength && _placed.Count > 0 && (touches || boardEmpty) && lexicon.IsTerminal(node))
                found.Add(_placed.ToArray());

            if (!board.InBounds(pos) || board.IsBlocked(pos))
                return;

            for (int i = 0; i < 26; i++)
            {
                if (counts[i] == 0)
                    continue;
                char letter = (char)('A' + i);
                int next = lexicon.Step(node, letter);
                if (next == IWordGraph.NoNode)
                    continue;

                var cross = CrossWord(pos, dir.Perpendicular(), letter);
                if (cross is not null && (cross.Length < minWordLength || !lexicon.Contains(cross)))
                    continue;

                counts[i]--;
                _placed.Add((pos, letter));
                Extend(pos.Step(dir), dir, next, length + 1, touches || cross is not null, found);
                _placed.RemoveAt(_placed.Count - 1);
                counts[i]++;
            }
        }

        /// <summary>The perpendicular word formed by putting <paramref name="letter"/> at <paramref name="pos"/>, or null if none.</summary>
        private string? CrossWord(Position pos, Direction dir, char letter)
        {
            var start = pos;
            while (board.IsOccupied(start.Step(dir, -1)))
                start = start.Step(dir, -1);
            if (start == pos && !board.IsOccupied(pos.Step(dir)))
                return null;

            var sb = new StringBuilder();
            for (var p = start; p == pos || board.IsOccupied(p); p = p.Step(dir))
                sb.Append(p == pos ? letter : board.TileAt(p)!.Letter.Char);
            return sb.ToString();
        }
    }

    private static List<PlacedTile> AssignTiles(Hand hand, IReadOnlyList<(Position Position, char Letter)> candidate)
    {
        var available = hand.Tiles.ToList();
        var placed = new List<PlacedTile>(candidate.Count);
        foreach (var (position, letter) in candidate)
        {
            var tile = available.First(t => t.Letter.Char == letter);
            available.Remove(tile);
            placed.Add(new PlacedTile(position, tile));
        }
        return placed;
    }

    private static string Key(IEnumerable<PlacedTile> placed) =>
        string.Join(';', placed.Select(p => $"{p.Position.Row},{p.Position.Col}{p.Tile.Letter.Char}").Order(StringComparer.Ordinal));
}
