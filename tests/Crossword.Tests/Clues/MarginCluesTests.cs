using System.Collections.Immutable;
using Crossword.Core.Clues;
using Crossword.Core.Domain;
using Crossword.Core.Profile;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Clues;

public class MarginCluesTests
{
    private static string? Define(string word) => word == "CAT" ? "a small domesticated feline" : null;

    // CAT across and CAR down share number 1; TOE down from the T is number 2:
    //   C A T
    //   A . O
    //   R . E
    private static readonly Board Grid = BoardFromRows("CAT..", "A.O..", "R.E..", ".....", ".....");

    [Fact]
    public void BoardWords_AreNumberedLikeAPrintedCrossword()
    {
        var words = BoardWords.Numbered(Grid);

        Assert.Equal(
            [(1, Direction.Across, "CAT"), (1, Direction.Down, "CAR"), (2, Direction.Down, "TOE")],
            words.Select(w => (w.Number, w.Direction, w.Text)));
        Assert.Equal(new Position(0, 2), words[2].Start);
    }

    [Fact]
    public void BoardWords_IgnoreSingleTiles()
    {
        Assert.Empty(BoardWords.Numbered(BoardFromRows("C....", ".....", "..A..", ".....", ".....")));
    }

    [Fact]
    public void FreshProfile_ReturnsFullDefaultClues()
    {
        var sheet = MarginClues.For(Board.Empty(5), PlayerStats.Empty, Define, slotsPerColumn: 10);

        foreach (var column in new[] { sheet.Across, sheet.Down })
        {
            Assert.Equal(10, column.Length);
            Assert.All(column, c =>
            {
                Assert.Equal(ClueKind.Tip, c.Kind);
                Assert.False(string.IsNullOrWhiteSpace(c.Answer));
                Assert.False(string.IsNullOrWhiteSpace(c.Text));
                Assert.Null(c.Number);
            });
        }
        Assert.Equal(NewsroomClues.Across, sheet.Across);
    }

    [Fact]
    public void BoardWordsComeFirst_WithDefinitionsOrTheFallback()
    {
        var sheet = MarginClues.For(Grid, PlayerStats.Empty, Define, slotsPerColumn: 4);

        Assert.Equal(new MarginClue(ClueKind.Board, 1, "CAT", "a small domesticated feline"), sheet.Across[0]);
        Assert.Equal([(1, "CAR"), (2, "TOE")], sheet.Down.Take(2).Select(c => (c.Number!.Value, c.Answer)));
        Assert.Equal(MarginClues.NoDefinition, sheet.Down[0].Text);
        Assert.Equal(4, sheet.Across.Length);
        Assert.Equal(ClueKind.Tip, sheet.Across[^1].Kind);
    }

    [Fact]
    public void StatUpdate_OverwritesDefaultClue()
    {
        var fresh = MarginClues.For(Board.Empty(5), PlayerStats.Empty, Define, slotsPerColumn: 6);
        var stats = StatsRules.RecordRunStart(PlayerStats.Empty);
        stats = StatsRules.RecordPlay(stats, PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT"), score: 99);

        var after = MarginClues.For(Board.Empty(5), stats, Define, slotsPerColumn: 6);

        Assert.Equal(ClueKind.Tip, fresh.Down[0].Kind);
        Assert.Equal(new MarginClue(ClueKind.Record, null, "1 RUN", "Editorial shifts logged at the press"), after.Down[0]);
        Assert.Equal(("CAT", ClueKind.Record), (after.Across[0].Answer, after.Across[0].Kind));
        Assert.Contains("3 letters", after.Across[0].Text);
        Assert.Equal(6, after.Across.Length);
        Assert.Equal(ClueKind.Tip, after.Across[^1].Kind); // records first, tips still fill the rest
    }

    [Fact]
    public void Records_CoverEveryTrackedStat()
    {
        var stats = PlayerStats.Empty with
        {
            Words = new Dictionary<string, WordUse> { ["OR"] = new(5, 1, 9), ["STARE"] = new(2, 2, 3), ["CATALOG"] = new(1, 3, 3) }
                .ToImmutableDictionary(),
            PlaysRecorded = 9,
            RunsStarted = 4,
            RunsWon = 1,
            BestWeekReached = 5,
            BestPlayScore = 1518,
            BestPlayWords = "CATALOG + OR",
            LongestWord = "CATALOG",
            TotalIntersections = 12,
            CloseCalls = 2,
            BossesBeaten = new Dictionary<string, int> { ["Ink Spill"] = 3, ["Tight Margins"] = 1 }.ToImmutableDictionary(),
            FullSpreadRounds = 1,
        };

        var across = MarginClues.AcrossRecords(stats).Select(c => c.Answer).ToList();
        var down = MarginClues.DownRecords(stats).Select(c => c.Answer).ToList();

        Assert.Equal(["CATALOG", "CATALOG", "STARE", "CATALOG", "3 DISTINCT", "OR"], across);
        Assert.Equal(["4 RUNS", "WEEK 5", "9 PLAYS", "OR", "12 CROSSES", "1 WIN", "2 CLOSE CALLS", "INK SPILL", "1 FULL SPREAD"], down);

        var sheet = MarginClues.For(BoardFromRows("OR...", ".....", ".....", ".....", "....."), stats, Define, slotsPerColumn: 12);
        Assert.Equal(["OR", "CATALOG", "STARE", "3 DISTINCT"], sheet.Across.Take(4).Select(c => c.Answer)); // no repeats
        Assert.Equal(sheet.Across.Length, sheet.Across.Select(c => c.Answer).Distinct().Count());
    }
}
