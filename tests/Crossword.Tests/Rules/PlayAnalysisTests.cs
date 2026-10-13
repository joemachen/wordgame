using Crossword.Core.Domain;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Rules;

public class PlayAnalysisTests
{
    [Fact]
    public void WithTile_ReplacesTheTile_InPlacedTilesWordsAndBoard()
    {
        // TO under AT in CAT: the placed T (hand tile) is in TO across and AT down.
        var play = PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");
        var placedT = play.Placed.Single(p => p.Tile.Letter.Char == 'T').Tile;
        var underlined = placedT with { Enhancement = TileEnhancement.Underlined };

        var swapped = play.WithTile(underlined);

        Assert.Equal(underlined, swapped.Placed.Single(p => p.Tile.Id == placedT.Id).Tile);
        Assert.Equal(2, swapped.Words.Count(w => w.Cells.Any(c => c.Tile == underlined)));
        Assert.Equal(underlined, swapped.BoardAfter.TileAt(new Position(1, 1)));
        Assert.Equal(play.Words.Select(w => w.Text), swapped.Words.Select(w => w.Text));
        Assert.DoesNotContain(play.Words, w => w.Cells.Any(c => c.Tile.Enhancement != TileEnhancement.None)); // the original is untouched
    }

    [Fact]
    public void WithTile_ReplacesABoardTile_TheWordsReuse()
    {
        var play = PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");
        var boardA = play.BoardAfter.TileAt(new Position(0, 1))!;
        var bold = boardA with { Enhancement = TileEnhancement.Bold };

        var swapped = play.WithTile(bold);

        Assert.Equal(bold, swapped.BoardAfter.TileAt(new Position(0, 1)));
        Assert.Contains(swapped.Words, w => w.Text == "AT" && w.Cells[0].Tile == bold);
        Assert.Equal(play.Placed, swapped.Placed);
    }

    [Fact]
    public void WithTile_ReturnsThePlayItself_WhenTheTileIsNotInIt()
    {
        var play = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

        Assert.Same(play, play.WithTile(new Tile(999, Letter.From('Z'), TileEnhancement.Bold)));
    }
}
