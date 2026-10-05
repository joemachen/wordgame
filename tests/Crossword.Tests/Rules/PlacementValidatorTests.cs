using Crossword.Core.Domain;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Rules;

public class PlacementValidatorTests
{
    private static Result<PlayAnalysis, PlacementError> Validate(Board board, Hand hand, IReadOnlyList<PlacedTile> placed) =>
        PlacementValidator.Validate(board, hand, placed, Words);

    private static PlacedTile PlacementTileAt(int row, int col, Tile tile) => new(new Position(row, col), tile);

    private static T AssertFails<T>(Result<PlayAnalysis, PlacementError> result) where T : PlacementError
    {
        Assert.False(result.IsOk, "Expected placement to be rejected.");
        return Assert.IsType<T>(result.Error);
    }

    [Fact]
    public void FirstPlay_AnywhereOnEmptyBoard_IsValid()
    {
        var board = Board.Empty(5);
        var hand = HandOf("CATXYZE");

        var result = Validate(board, hand, Spell(board, hand, 4, 2, Direction.Across, "CAT"));

        Assert.True(result.IsOk);
        Assert.Equal(["CAT"], result.Value.Words.Select(w => w.Text));
        Assert.Equal("T", result.Value.BoardAfter.TileAt(new Position(4, 4))!.ToString());
    }

    [Fact]
    public void FirstPlay_SingleTile_FormsNoWord()
    {
        var board = Board.Empty(5);
        var hand = HandOf("C");

        AssertFails<PlacementError.NoWordFormed>(Validate(board, hand, Spell(board, hand, 2, 2, Direction.Across, "C")));
    }

    [Fact]
    public void EmptyPlacement_IsRejected()
    {
        AssertFails<PlacementError.NoTiles>(Validate(Board.Empty(5), HandOf("CAT"), []));
    }

    [Fact]
    public void TileNotInHand_IsRejected()
    {
        var stranger = new Tile(99, Letter.From('Q'));

        AssertFails<PlacementError.TileNotInHand>(
            Validate(Board.Empty(5), HandOf("CAT"), [new PlacedTile(new Position(0, 0), stranger)]));
    }

    [Fact]
    public void SameTileTwice_IsRejected()
    {
        var hand = HandOf("CAT");
        var c = hand.Tiles[0];

        AssertFails<PlacementError.DuplicateTile>(Validate(Board.Empty(5), hand,
            [new PlacedTile(new Position(0, 0), c), new PlacedTile(new Position(0, 1), c)]));
    }

    [Fact]
    public void OffBoard_IsRejected()
    {
        var hand = HandOf("CAT");

        AssertFails<PlacementError.OutOfBounds>(Validate(Board.Empty(3), hand,
            [new PlacedTile(new Position(0, 2), hand.Tiles[0]), new PlacedTile(new Position(0, 3), hand.Tiles[1])]));
    }

    [Fact]
    public void TwoTilesOnOneCell_IsRejected()
    {
        var hand = HandOf("CAT");

        AssertFails<PlacementError.DuplicatePosition>(Validate(Board.Empty(5), hand,
            [new PlacedTile(new Position(1, 1), hand.Tiles[0]), new PlacedTile(new Position(1, 1), hand.Tiles[1])]));
    }

    [Fact]
    public void OnOccupiedCell_IsRejected()
    {
        var board = BoardFromRows(".....", ".CAT.", ".....", ".....", ".....");
        var hand = HandOf("S");

        AssertFails<PlacementError.Occupied>(Validate(board, hand, [new PlacedTile(new Position(1, 1), hand.Tiles[0])]));
    }

    [Fact]
    public void Diagonal_IsRejected()
    {
        var hand = HandOf("AT");

        AssertFails<PlacementError.NotInLine>(Validate(Board.Empty(5), hand,
            [new PlacedTile(new Position(0, 0), hand.Tiles[0]), new PlacedTile(new Position(1, 1), hand.Tiles[1])]));
    }

    [Fact]
    public void GapInLine_IsRejected()
    {
        var hand = HandOf("CT");

        var gap = AssertFails<PlacementError.Gap>(Validate(Board.Empty(5), hand,
            [new PlacedTile(new Position(0, 0), hand.Tiles[0]), new PlacedTile(new Position(0, 2), hand.Tiles[1])]));
        Assert.Equal(new Position(0, 1), gap.Position);
    }

    [Fact]
    public void GapBridgedByExistingTile_IsValid()
    {
        // Existing A at (1,1); play C at (1,0) and T at (1,2) to make CAT.
        var board = BoardFromRows(".....", ".A...", ".....", ".....", ".....");
        var hand = HandOf("CT");

        var result = Validate(board, hand, Spell(board, hand, 1, 0, Direction.Across, "CAT"));

        Assert.True(result.IsOk);
        Assert.Equal(["CAT"], result.Value.Words.Select(w => w.Text));
        Assert.Equal([true, false, true], result.Value.Words[0].Cells.Select(c => c.IsNew));
    }

    [Fact]
    public void Disconnected_IsRejected()
    {
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var hand = HandOf("AT");

        AssertFails<PlacementError.NotConnected>(Validate(board, hand, Spell(board, hand, 4, 0, Direction.Across, "AT")));
    }

    [Fact]
    public void ExtendingWord_ScoresWholeWord()
    {
        var board = BoardFromRows(".....", "CAT..", ".....", ".....", ".....");
        var hand = HandOf("S");

        var result = Validate(board, hand, Spell(board, hand, 1, 0, Direction.Across, "CATS"));

        Assert.True(result.IsOk);
        Assert.Equal(["CATS"], result.Value.Words.Select(w => w.Text));
    }

    [Fact]
    public void SingleTile_FormingAcrossAndDown_ReturnsBothWords_AndIsAnIntersection()
    {
        //   row0: C A T
        //   row1: A S .   ← new S at (1,1) forms AS across and AS down
        var board = BoardFromRows("CAT..", "A....", ".....", ".....", ".....");
        var hand = HandOf("S");

        var result = Validate(board, hand, [new PlacedTile(new Position(1, 1), hand.Tiles[0])]);

        Assert.True(result.IsOk);
        Assert.Equal(["AS", "AS"], result.Value.Words.Select(w => w.Text));
        Assert.Equal([Direction.Across, Direction.Down], result.Value.Words.Select(w => w.Direction));
        Assert.Equal([new Position(1, 1)], result.Value.Intersections);
    }

    [Fact]
    public void ParallelPlay_FormsCrossWords()
    {
        //   row0: C A T
        //   row1: . T O   ← new "TO" under "AT" forms AT (down) and TO (down)
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var hand = HandOf("TO");

        var result = Validate(board, hand, Spell(board, hand, 1, 1, Direction.Across, "TO"));

        Assert.True(result.IsOk);
        Assert.Equal(["TO", "AT", "TO"], result.Value.Words.Select(w => w.Text));
        Assert.Equal(2, result.Value.Intersections.Length);
    }

    [Fact]
    public void InvalidCrossWord_IsRejected_ListingIt()
    {
        //   row0: C A T
        //   row1: . Z A   ← ZA across and TA down are valid, but AZ down is not
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var hand = HandOf("ZA");

        var error = AssertFails<PlacementError.InvalidWords>(Validate(board, hand, Spell(board, hand, 1, 1, Direction.Across, "ZA")));
        Assert.Equal(["AZ"], error.Words);
    }

    [Fact]
    public void PlacingOnBlockedCell_IsRejected()
    {
        var board = Board.Empty(5) with { Blocked = [new Position(0, 1)] };
        var hand = HandOf("AT");

        AssertFails<PlacementError.Blocked>(Validate(board, hand,
            [PlacementTileAt(0, 0, hand.Tiles[0]), PlacementTileAt(0, 1, hand.Tiles[1])]));
    }

    [Fact]
    public void CannotExtendWordIntoBlockedCell()
    {
        var board = BoardFromRows(".....", "CAT..", ".....", ".....", ".....") with { Blocked = [new Position(1, 3)] };
        var hand = HandOf("S");

        AssertFails<PlacementError.Blocked>(Validate(board, hand, [PlacementTileAt(1, 3, hand.Tiles[0])]));
    }

    [Fact]
    public void BlockedCellInsideSpan_IsAGap()
    {
        var board = Board.Empty(5) with { Blocked = [new Position(0, 1)] };
        var hand = HandOf("CT");

        AssertFails<PlacementError.Gap>(Validate(board, hand,
            [PlacementTileAt(0, 0, hand.Tiles[0]), PlacementTileAt(0, 2, hand.Tiles[1])]));
    }

    [Fact]
    public void MinWordLength_RejectsShortCrossWords()
    {
        //   row0: C A T
        //   row1: . T O   → TO, AT, TO are all 2 letters
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var hand = HandOf("TO");

        var result = PlacementValidator.Validate(board, hand, Spell(board, hand, 1, 1, Direction.Across, "TO"), Words, minWordLength: 3);

        var error = AssertFails<PlacementError.WordsTooShort>(result);
        Assert.Equal(["TO", "AT"], error.Words);
    }

    [Fact]
    public void WordRunningToBoardEdge_IsExtracted()
    {
        var board = BoardFromRows("....", "....", "....", "CAT.");
        var hand = HandOf("S");

        var result = Validate(board, hand, Spell(board, hand, 3, 0, Direction.Across, "CATS"));

        Assert.True(result.IsOk);
        Assert.Equal(["CATS"], result.Value.Words.Select(w => w.Text));
    }
}
