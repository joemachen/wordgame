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
        UnderlinedMult = 1.5m,
        EmbossedChips = 2m,
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
    public void UnderlinedTile_MultipliesMult_OncePerWordItIsIn()
    {
        // T goes under A in CAT, forming TO across and AT down: the Underlined T is in both words → ×1.5 × 1.5.
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var hand = new Hand([new Tile(0, Letter.From('T'), TileEnhancement.Underlined), new Tile(1, Letter.From('O'))]);
        var play = Validate(board, hand, Spell(board, hand, 1, 1, Direction.Across, "TO"));

        var plain = ScoringEngine.Score(play, [], Config with { UnderlinedMult = 1 });
        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(plain.Chips, score.Chips);
        Assert.Equal(plain.Mult * 2.25m, score.Mult);
        Assert.Equal(2, score.Log.Count(e => e.SourceId == ScoringEngine.Sources.Enhancement));
    }

    [Fact]
    public void EmbossedTile_MultipliesChips_OncePerWordItIsIn()
    {
        var hand = new Hand([new Tile(0, Letter.From('C'), TileEnhancement.Embossed), new Tile(1, Letter.From('A')), new Tile(2, Letter.From('T'))]);
        var play = Validate(Board.Empty(5), hand, Spell(Board.Empty(5), hand, 0, 0, Direction.Across, "CAT"));

        var plain = ScoringEngine.Score(play, [], Config with { EmbossedChips = 1 });
        var score = ScoringEngine.Score(play, [], Config);

        Assert.Equal(plain.Chips * 2, score.Chips);
        Assert.Equal(plain.Mult, score.Mult);
    }

    [Fact]
    public void MultiplicativeEnhancements_FireAfterEveryAddition_AndBeforeDeskItems()
    {
        // Bold C (+10 chips) and Underlined T (×1.5 mult) in one play with an intersection and Red Ink: the Underlined
        // step comes after the Bold, intersection and Red Ink steps and before the Red Pen, so it multiplies them all.
        var board = Enhance(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), 0, 0, TileEnhancement.Bold);
        var hand = new Hand([new Tile(0, Letter.From('S'), TileEnhancement.Underlined), new Tile(1, Letter.From('A')), new Tile(2, Letter.From('T'))]);
        var play = Validate(board, hand, Spell(board, hand, 0, 3, Direction.Down, "SAT")); // CATS across + SAT down, S in both

        var score = ScoringEngine.Score(play, [new Crossword.Core.DeskItems.RedPen(2)], Config with { BonusMult = 3 });

        var sources = score.Log.Select(e => e.SourceId).ToList();
        int underlined = sources.LastIndexOf(ScoringEngine.Sources.Enhancement);
        Assert.True(underlined > sources.IndexOf(ScoringEngine.Sources.Intersection), "after intersections");
        Assert.True(underlined > sources.IndexOf(ScoringEngine.Sources.Bonus), "after Red Ink");
        Assert.True(underlined < sources.IndexOf("red-pen"), "before Desk Items");
        // Tier 10 × 2 (CATS), + letters, Bold +10; mult 2 + 2 (intersection) + 3 (red ink) = 7, ×1.5 ×1.5 = 15.75, + 2 = 17.75.
        Assert.Equal(17.75m, score.Mult);
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
