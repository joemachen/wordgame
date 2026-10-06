using System.Collections.Immutable;
using System.Text;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;

namespace Crossword.Core.Analysis;

/// <summary>
/// Enumerates every legal play for a board and hand. For each line and start cell it walks the word
/// graph letter by letter — consuming board letters and trying hand letters on empty cells — pruning
/// dead prefixes and invalid cross words immediately. Wild tiles fill in for a letter only when no real tile of that
/// letter is left (they score 0 chips). A censored letter is never placed. Results are re-checked by
/// <see cref="PlacementValidator"/>.
/// </summary>
public static class MoveGenerator
{
    public static IEnumerable<PlayAnalysis> LegalPlays(Board board, Hand hand, IWordGraph lexicon, int minWordLength = 2,
        char? censoredLetter = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in Candidates(board, hand, lexicon, minWordLength, censoredLetter))
        {
            var placed = AssignTiles(hand, candidate);
            if (!seen.Add(Key(placed)))
                continue; // single-tile plays are found once per direction; wild-letter variants once per placement

            var result = PlacementValidator.Validate(board, hand, placed, lexicon, minWordLength, censoredLetter);
            if (result.IsOk)
                yield return result.Value;
        }
    }

    public static bool HasLegalPlay(Board board, Hand hand, IWordGraph lexicon, int minWordLength = 2, char? censoredLetter = null) =>
        LegalPlays(board, hand, lexicon, minWordLength, censoredLetter).Any();

    private static IEnumerable<IReadOnlyList<(Position Position, char Letter, bool Wild)>> Candidates(
        Board board, Hand hand, IWordGraph lexicon, int minWordLength, char? censoredLetter)
    {
        var counts = new int[26];
        int wilds = 0;
        foreach (var tile in hand.Tiles)
        {
            if (tile.IsWild)
                wilds++;
            else
                counts[tile.Letter.Char - 'A']++;
        }

        int censored = censoredLetter is { } c ? char.ToUpperInvariant(c) - 'A' : -1;
        var search = new Search(board, lexicon, counts, wilds, hand.Count, board.IsEmpty, minWordLength, censored);
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

    private sealed class Search(Board board, IWordGraph lexicon, int[] counts, int wilds, int handSize, bool boardEmpty, int minWordLength,
        int censored)
    {
        private readonly Dictionary<(Position, Direction), int> _fillsToTouch = new();
        private readonly List<(Position, char, bool)> _placed = new();
        private readonly Dictionary<(Position, Direction), (uint Allowed, bool HasCross)> _crossChecks = new();
        private int _wilds = wilds;

        public List<IReadOnlyList<(Position, char, bool)>> From(Position start, Direction direction)
        {
            var found = new List<IReadOnlyList<(Position, char, bool)>>();
            Extend(start, direction, lexicon.Root, length: 0, touches: false, found);
            return found;
        }

        private void Extend(Position pos, Direction dir, int node, int length, bool touches,
            List<IReadOnlyList<(Position, char, bool)>> found)
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

            // A play must touch the board: give up when the tiles left can't reach a square that touches it.
            if (!touches && !boardEmpty && FillsToTouch(pos, dir) > handSize - _placed.Count)
                return;

            for (int i = 0; i < 26; i++)
            {
                if (i == censored)
                    continue;
                bool wild = counts[i] == 0;
                if (wild && _wilds == 0)
                    continue;
                char letter = (char)('A' + i);
                int next = lexicon.Step(node, letter);
                if (next == IWordGraph.NoNode)
                    continue;

                var (allowed, hasCross) = CrossCheck(pos, dir.Perpendicular());
                if (hasCross && (allowed & (1u << i)) == 0)
                    continue;

                if (wild)
                    _wilds--;
                else
                    counts[i]--;
                _placed.Add((pos, letter, wild));
                Extend(pos.Step(dir), dir, next, length + 1, touches || hasCross, found);
                _placed.RemoveAt(_placed.Count - 1);
                if (wild)
                    _wilds++;
                else
                    counts[i]++;
            }
        }

        /// <summary>
        /// How many empty squares from <paramref name="pos"/> along <paramref name="dir"/> must be filled before the word
        /// touches the board (reaches a tile, or fills a square next to one); <see cref="int.MaxValue"/> if it never can.
        /// </summary>
        private int FillsToTouch(Position pos, Direction dir)
        {
            if (_fillsToTouch.TryGetValue((pos, dir), out int cached))
                return cached;
            int fills = 0;
            var cross = dir.Perpendicular();
            for (var p = pos; ; p = p.Step(dir))
            {
                if (!board.InBounds(p) || board.IsBlocked(p))
                {
                    fills = int.MaxValue;
                    break;
                }
                if (board.IsOccupied(p))
                    break;
                fills++;
                if (board.IsOccupied(p.Step(cross)) || board.IsOccupied(p.Step(cross, -1)))
                    break;
            }
            return _fillsToTouch[(pos, dir)] = fills;
        }

        /// <summary>
        /// Which letters may go at <paramref name="pos"/> given the perpendicular word they would form (bit i = 'A' + i),
        /// computed once per cell and direction; <c>HasCross</c> is false when no perpendicular word forms there.
        /// </summary>
        private (uint Allowed, bool HasCross) CrossCheck(Position pos, Direction dir)
        {
            if (_crossChecks.TryGetValue((pos, dir), out var cached))
                return cached;
            uint allowed = 0;
            bool hasCross = false;
            for (int i = 0; i < 26; i++)
            {
                var cross = CrossWord(pos, dir, (char)('A' + i));
                if (cross is null)
                    break; // no perpendicular neighbours: no cross word for any letter
                hasCross = true;
                if (cross.Length >= minWordLength && lexicon.Contains(cross))
                    allowed |= 1u << i;
            }
            return _crossChecks[(pos, dir)] = (allowed, hasCross);
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

    private static List<PlacedTile> AssignTiles(Hand hand, IReadOnlyList<(Position Position, char Letter, bool Wild)> candidate)
    {
        var available = hand.Tiles.ToList();
        var placed = new List<PlacedTile>(candidate.Count);
        foreach (var (position, letter, wild) in candidate)
        {
            var tile = wild
                ? available.First(t => t.IsWild)
                : available.First(t => !t.IsWild && t.Letter.Char == letter);
            available.Remove(tile);
            placed.Add(new PlacedTile(position, wild ? tile.As(Letter.From(letter)) : tile));
        }
        return placed;
    }

    /// <summary>
    /// Identifies a placement by squares and tiles. Plays that differ only in the letter a wild tile stands for share a
    /// key, so only the first (alphabetically) is kept: they score the same, since a wild is worth 0 chips whatever its
    /// letter, and without this a hand with two wilds has ~20× as many plays to validate and score.
    /// </summary>
    private static string Key(IEnumerable<PlacedTile> placed) =>
        string.Join(';', placed.Select(p => $"{p.Position.Row},{p.Position.Col}#{p.Tile.Id}").Order(StringComparer.Ordinal));
}
