using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Analysis;

public class MoveGeneratorTests
{
    private static string Key(IEnumerable<PlacedTile> placed) =>
        string.Join(';', placed.Select(p => $"{p.Position}{p.Tile.Letter.Char}").Order(StringComparer.Ordinal));

    /// <summary>Exhaustive reference: every subset of empty cells in every line × every tile arrangement.</summary>
    private static HashSet<string> BruteForce(Board board, Hand hand, ILexicon lexicon, int minWordLength = 2)
    {
        var valid = new HashSet<string>();
        foreach (var direction in new[] { Direction.Across, Direction.Down })
        {
            for (int line = 0; line < board.Size; line++)
            {
                var cells = Enumerable.Range(0, board.Size)
                    .Select(i => direction == Direction.Across ? new Position(line, i) : new Position(i, line))
                    .Where(p => !board.IsOccupied(p) && !board.IsBlocked(p))
                    .ToList();

                for (int mask = 1; mask < 1 << cells.Count; mask++)
                {
                    var chosen = cells.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                    if (chosen.Count > hand.Count)
                        continue;
                    foreach (var arrangement in Arrangements(hand.Tiles.ToList(), chosen.Count))
                    {
                        var placed = chosen.Zip(arrangement, (p, t) => new PlacedTile(p, t)).ToList();
                        if (PlacementValidator.Validate(board, hand, placed, lexicon, minWordLength).IsOk)
                            valid.Add(Key(placed));
                    }
                }
            }
        }
        return valid;
    }

    private static IEnumerable<List<Tile>> Arrangements(List<Tile> tiles, int count)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }
        for (int i = 0; i < tiles.Count; i++)
        {
            var rest = tiles.Where((_, j) => j != i).ToList();
            foreach (var tail in Arrangements(rest, count - 1))
                yield return [tiles[i], .. tail];
        }
    }

    public static TheoryData<string[], string> Scenarios => new()
    {
        { ["....", "....", "....", "...."], "CATS" },
        { ["....", ".CAT", "....", "...."], "SAOE" },
        { ["C...", "A...", "T...", "...."], "SRAE" },
        { ["CAT.", "....", "..O.", "...."], "AERS" },
        { ["....", "....", "....", "...."], "ZAX" },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void LegalPlays_MatchesBruteForce(string[] rows, string handLetters)
    {
        var board = BoardFromRows(rows);
        var hand = HandOf(handLetters);

        var generated = MoveGenerator.LegalPlays(board, hand, Words).Select(p => Key(p.Placed)).ToHashSet();
        var expected = BruteForce(board, hand, Words);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Order(), generated.Order());
    }

    public static TheoryData<string[], string, int, int[]> ConstrainedScenarios => new()
    {
        // rows, hand, min word length, blocked cells as row*10+col
        { ["....", ".CAT", "....", "...."], "SAOE", 3, [] },
        { ["CAT.", "....", "..O.", "...."], "AERS", 3, [] },
        { ["....", ".CAT", "....", "...."], "SAOE", 2, [0, 33] },
        { ["....", "....", "....", "...."], "CATS", 2, [1, 32] },
        { ["C...", "A...", "T...", "...."], "SRAE", 3, [11, 22] },
    };

    [Theory]
    [MemberData(nameof(ConstrainedScenarios))]
    public void LegalPlays_WithMinLengthAndBlockedCells_MatchBruteForce(string[] rows, string handLetters, int minLength, int[] blocked)
    {
        var board = BoardFromRows(rows) with
        {
            Blocked = blocked.Select(b => new Position(b / 10, b % 10)).ToImmutableHashSet(),
        };
        var hand = HandOf(handLetters);

        var generated = MoveGenerator.LegalPlays(board, hand, Words, minLength).Select(p => Key(p.Placed)).ToHashSet();
        var expected = BruteForce(board, hand, Words, minLength);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Order(), generated.Order());
    }

    [Fact]
    public void LegalPlays_HaveNoDuplicates()
    {
        var board = BoardFromRows("....", ".CAT", "....", "....");

        var keys = MoveGenerator.LegalPlays(board, HandOf("SAOE"), Words).Select(p => Key(p.Placed)).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void HasLegalPlay_FalseWhenHandCannotFormAnything()
    {
        var board = BoardFromRows("....", ".CAT", "....", "....");

        Assert.False(MoveGenerator.HasLegalPlay(board, HandOf("QQQ"), Words));
        Assert.True(MoveGenerator.HasLegalPlay(board, HandOf("S"), Words));
    }

    [Fact]
    public void LegalPlays_OnFullEnableLexicon_AllValidate()
    {
        var board = BoardFromRows(".......", ".......", ".......", "..CRANE", ".......", ".......", ".......");
        var hand = HandOf("SETRAIN");

        var plays = MoveGenerator.LegalPlays(board, hand, LexiconLoader.Enable).ToList();

        Assert.True(plays.Count > 100, $"Only {plays.Count} plays found");
        Assert.All(plays, p => Assert.True(PlacementValidator.Validate(board, hand, p.Placed, LexiconLoader.Enable).IsOk));
    }
}
