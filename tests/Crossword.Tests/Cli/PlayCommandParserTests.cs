using Crossword.Cli;
using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Stationery;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Cli;

public class PlayCommandParserTests
{
    [Theory]
    [InlineData("A1", 0, 0)]
    [InlineData("c4", 3, 2)]
    [InlineData("G7", 6, 6)]
    public void ParseCell_ReadsColumnLetterAndRowNumber(string text, int row, int col)
    {
        Assert.Equal(new Position(row, col), PlayCommandParser.ParseCell(text).Value);
    }

    [Theory]
    [InlineData("4C")]
    [InlineData("C0")]
    [InlineData("C")]
    public void ParseCell_RejectsMalformedCells(string text)
    {
        Assert.False(PlayCommandParser.ParseCell(text).IsOk);
    }

    [Fact]
    public void ParsePlay_ParsesFullCommand()
    {
        var command = PlayCommandParser.ParsePlay(["C4", "d", "crane"]).Value;

        Assert.Equal(new PlayCommand(new Position(3, 2), Direction.Down, "CRANE"), command);
    }

    [Theory]
    [InlineData("C4", "x", "CAT")]
    [InlineData("C4", "a", "C4T")]
    public void ParsePlay_RejectsBadDirectionOrWord(string cell, string dir, string word)
    {
        Assert.False(PlayCommandParser.ParsePlay([cell, dir, word]).IsOk);
    }

    [Fact]
    public void ParsePlay_RejectsWrongArgumentCount()
    {
        Assert.False(PlayCommandParser.ParsePlay(["C4", "a"]).IsOk);
    }

    [Fact]
    public void ResolveTiles_SkipsExistingLetters_AndTakesRestFromHand()
    {
        var board = BoardFromRows(".....", ".A...", ".....", ".....", ".....");
        var hand = HandOf("CTX");

        var placed = PlayCommandParser.ResolveTiles(board, hand, new PlayCommand(new Position(1, 0), Direction.Across, "CAT")).Value;

        Assert.Equal([new Position(1, 0), new Position(1, 2)], placed.Select(p => p.Position));
        Assert.Equal("CT", string.Concat(placed.Select(p => p.Tile.Letter.Char)));
    }

    [Fact]
    public void ResolveTiles_RejectsMismatchWithBoard()
    {
        var board = BoardFromRows(".....", ".O...", ".....", ".....", ".....");

        var result = PlayCommandParser.ResolveTiles(board, HandOf("CAT"), new PlayCommand(new Position(1, 0), Direction.Across, "CAT"));

        Assert.Contains("already has O", result.Error);
    }

    [Fact]
    public void ResolveTiles_RejectsMissingLetter_AndOffBoard()
    {
        var board = Board.Empty(3);

        Assert.Contains("No 'Z'", PlayCommandParser.ResolveTiles(board, HandOf("CAT"), new PlayCommand(new Position(0, 0), Direction.Across, "ZA")).Error);
        Assert.Contains("off the board", PlayCommandParser.ResolveTiles(board, HandOf("CATS"), new PlayCommand(new Position(0, 0), Direction.Across, "CATS")).Error);
    }

    [Fact]
    public void ResolveDiscard_HandlesDuplicateLetters()
    {
        var hand = HandOf("EEX");

        Assert.Equal([0, 1], PlayCommandParser.ResolveDiscard(hand, "ee").Value);
        Assert.False(PlayCommandParser.ResolveDiscard(hand, "EEE").IsOk);
    }

    [Fact]
    public void ResolveDeckTiles_PrefersPlainTiles()
    {
        Tile[] deck = [new(0, Letter.From('E'), TileEnhancement.Bold), new(1, Letter.From('E')), new(2, Letter.From('Q'))];

        Assert.Equal([1, 2], PlayCommandParser.ResolveDeckTiles(deck, "eq").Value);
        Assert.Equal([1, 0], PlayCommandParser.ResolveDeckTiles(deck, "EE").Value);
        Assert.False(PlayCommandParser.ResolveDeckTiles(deck, "Z").IsOk);
    }

    [Fact]
    public void ParseStationeryUse_NoTargetItemTakesNoArguments()
    {
        var hand = HandOf("CAT");
        var parsed = PlayCommandParser.ParseStationeryUse(new MarginClip(), hand, []);
        Assert.Null(parsed.Value.TileIds);
        Assert.Null(parsed.Value.Cell);
        Assert.False(PlayCommandParser.ParseStationeryUse(new AnswerKey(), hand, ["C"]).IsOk);
    }

    [Fact]
    public void ParseStationeryUse_HandTileItemPicksTilesByLetter()
    {
        var hand = new Hand([new Tile(0, Letter.From('Q')), new Tile(1, Letter.From('E')), new Tile(2, Letter.From('E')), Tile.Wild(3)]);
        Assert.Equal([1, 2], PlayCommandParser.ParseStationeryUse(new Scissors(), hand, ["ee"]).Value.TileIds);
        Assert.Equal([3], PlayCommandParser.ParseStationeryUse(new FountainPen(), hand, ["?"]).Value.TileIds);

        var missing = PlayCommandParser.ParseStationeryUse(new Scissors(), hand, ["Z"]);
        Assert.Contains("No 'Z' left in your hand", missing.Error);
        Assert.False(PlayCommandParser.ParseStationeryUse(new Scissors(), hand, []).IsOk);
    }

    [Fact]
    public void ParseStationeryUse_BoardTileItemReadsACell()
    {
        var hand = HandOf("CAT");
        Assert.Equal(new Position(3, 3), PlayCommandParser.ParseStationeryUse(new WhiteOut(), hand, ["d4"]).Value.Cell);
        Assert.False(PlayCommandParser.ParseStationeryUse(new WhiteOut(), hand, ["4D"]).IsOk);
        Assert.False(PlayCommandParser.ParseStationeryUse(new WhiteOut(), hand, []).IsOk);
    }

    [Fact]
    public void ParseStationeryUse_EmptyCellAndBoardWordItemsReadACell()
    {
        var hand = HandOf("CAT");
        Assert.Equal(new Position(1, 2), PlayCommandParser.ParseStationeryUse(new GoldStar(), hand, ["c2"]).Value.Cell);
        Assert.Equal(new Position(0, 0), PlayCommandParser.ParseStationeryUse(new Clipping(), hand, ["A1"]).Value.Cell);
        Assert.False(PlayCommandParser.ParseStationeryUse(new GoldStar(), hand, []).IsOk);
        Assert.Null(PlayCommandParser.ParseStationeryUse(new PoeticLicense(), hand, []).Value.Cell);
    }
}
