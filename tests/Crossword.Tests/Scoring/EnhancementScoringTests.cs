using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Scoring;

[Trait("Category", "Scoring")]
public class EnhancementScoringTests
{
    private static readonly ScoringConfig Config = ScoringConfig.Default with
    {
        Tiers = [new WordTier(2, 2, 1), new WordTier(3, 5, 1), new WordTier(4, 10, 2)],
        IntersectionMult = 2,
        BoldChips = 10,
        ItalicMult = 2,
        GildedMoney = 1,
    };

    private static PlayAnalysis Validate(Board board, Hand hand, IReadOnlyList<PlacedTile> placed)
    {
        var result = PlacementValidator.Validate(board, hand, placed, Words);
        Assert.True(result.IsOk, result.IsOk ? "" : result.Error.Message);
        return result.Value;
    }

    [Fact]
    public void BoldTile_InNewWord_AddsChipsOnce()
    {
        var hand = new Hand([new Tile(0, Letter.From('C'), TileEnhancement.Bold), new Tile(1, Letter.From('A')), new Tile(2, Letter.From('T'))]);
        var play = Validate(Board.Empty(5), hand, Spell(Board.Empty(5), hand, 0, 0, Direction.Across, "CAT"));

        var plain = ScoringEngine.Score(play, [], Config with { BoldChips = 0 });
        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(plain.Chips + 10, score.Chips);
        Assert.Single(score.Log, e => e.SourceId == ScoringEngine.Sources.Enhancement);
    }

    [Fact]
    public void EnhancedTileAtCrossing_TriggersOncePerWord()
    {
        // New S at (1,1) forms AS across and AS down; an Italic S is in both words.
        var board = BoardFromRows("CAT..", "A....", ".....", ".....", ".....");
        var hand = new Hand([new Tile(0, Letter.From('S'), TileEnhancement.Italic)]);
        var play = Validate(board, hand, [new PlacedTile(new Position(1, 1), hand.Tiles[0])]);

        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(1m + 2 * 2 + 1 * 2, score.Mult); // tier 1 + two Italic triggers + one intersection
    }

    [Fact]
    public void EnhancedTileAlreadyOnBoard_RetriggersWhenBuiltOn()
    {
        var board = Enhance(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), 0, 0, TileEnhancement.Bold);
        var hand = HandOf("S");
        var play = Validate(board, hand, Spell(board, hand, 0, 0, Direction.Across, "CATS"));

        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(10 + (3 + 1 + 1 + 1) + 10, score.Chips);
    }

    [Fact]
    public void GildedTile_EarnsMoney_WithoutChangingScore()
    {
        var board = Enhance(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), 0, 0, TileEnhancement.Gilded);
        var hand = HandOf("S");
        var play = Validate(board, hand, Spell(board, hand, 0, 0, Direction.Across, "CATS"));

        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(1, score.Money);
        Assert.Equal(16 * 2, score.Total);
    }

    [Fact]
    public void PlainTiles_ProduceNoEnhancementEvents()
    {
        var hand = HandOf("CAT");
        var play = Validate(Board.Empty(5), hand, Spell(Board.Empty(5), hand, 0, 0, Direction.Across, "CAT"));

        var score = ScoringEngine.Score(play, [], Config);

        Assert.DoesNotContain(score.Log, e => e.SourceId == ScoringEngine.Sources.Enhancement);
        Assert.Equal(0, score.Money);
    }
}
