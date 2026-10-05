using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.Effects.EffectPipelineTests;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Scoring;

[Trait("Category", "Scoring")]
public class ScoringEngineTests
{
    /// <summary>Fixed numbers so balance tuning of <see cref="ScoringConfig.Default"/> never breaks these tests.</summary>
    private static readonly ScoringConfig Config = ScoringConfig.Default with
    {
        Tiers =
        [
            new WordTier(2, 2, 1),
            new WordTier(3, 5, 1),
            new WordTier(4, 10, 2),
            new WordTier(5, 20, 3),
            new WordTier(6, 30, 4),
            new WordTier(7, 40, 5),
        ],
        IntersectionMult = 2,
    };

    private static Board WithPremiums(Board board, params (int Row, int Col, Premium Premium)[] premiums)
    {
        var layout = board.Premiums.ToBuilder();
        foreach (var (r, c, p) in premiums)
            layout[r * board.Size + c] = p;
        return board with { Premiums = layout.MoveToImmutable() };
    }

    private static PlayAnalysis Play(Board board, string hand, int row, int col, Direction dir, string word)
    {
        var h = HandOf(hand);
        var result = PlacementValidator.Validate(board, h, Spell(board, h, row, col, dir, word), Words);
        Assert.True(result.IsOk, result.IsOk ? "" : result.Error.Message);
        return result.Value;
    }

    private static ScoreContext Score(PlayAnalysis play, params IDeskItem[] items) =>
        ScoringEngine.Score(play, items, Config);

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(3, 5, 1)]
    [InlineData(5, 20, 3)]
    [InlineData(7, 40, 5)]
    [InlineData(12, 40, 5)]
    public void TierFor_PicksHighestSatisfiedTier(int length, long chips, int mult)
    {
        var tier = Config.TierFor(length);

        Assert.Equal(chips, tier.BaseChips);
        Assert.Equal(mult, tier.BaseMult);
    }

    [Fact]
    public void PlainWord_ScoresTierPlusLetters()
    {
        var play = Play(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

        var score = Score(play);

        Assert.Equal(5 + 3 + 1 + 1, score.Chips);
        Assert.Equal(1m, score.Mult);
        Assert.Equal(10, score.Total);
    }

    [Fact]
    public void LetterAndWordPremiums_UnderNewTiles_Apply()
    {
        // C on DL (3×2 = 6), A = 1, T on DW → word (6 + 1 + 1) × 2 = 16; plus tier 5.
        var board = WithPremiums(Board.Empty(5), (0, 0, Premium.DoubleLetter), (0, 2, Premium.DoubleWord));
        var play = Play(board, "CAT", 0, 0, Direction.Across, "CAT");

        var score = Score(play);

        Assert.Equal(21, score.Chips);
        Assert.Equal(21, score.Total);
    }

    [Fact]
    public void TripleLetterAndTripleWord_Apply()
    {
        // C on TL (9), A, T on TW → (9 + 1 + 1) × 3 = 33; plus tier 5.
        var board = WithPremiums(Board.Empty(5), (0, 0, Premium.TripleLetter), (0, 2, Premium.TripleWord));

        var score = Score(Play(board, "CAT", 0, 0, Direction.Across, "CAT"));

        Assert.Equal(38, score.Chips);
    }

    [Fact]
    public void Premiums_UnderExistingTiles_AreIgnored()
    {
        // CAT already sits on a TW; extending to CATS must not re-trigger it.
        var board = WithPremiums(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), (0, 0, Premium.TripleWord));
        var play = Play(board, "S", 0, 0, Direction.Across, "CATS");

        var score = Score(play);

        Assert.Equal(10 + 3 + 1 + 1 + 1, score.Chips); // 4-letter tier + letters, no ×3
        Assert.Equal(2m, score.Mult);
    }

    [Fact]
    public void TierComesFromLongestWord_OtherWordsAddChipsOnly()
    {
        //   row0: C A T
        //   row1: . T O   → TO across, AT down, TO down — all 2 letters → tier 2.
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var play = Play(board, "TO", 1, 1, Direction.Across, "TO");

        var score = Score(play);

        Assert.Equal(2 + 2 + 2 + 2, score.Chips);       // tier + TO + AT + TO
        Assert.Equal(1m + 2 * Config.IntersectionMult, score.Mult); // two intersecting new tiles
        Assert.Equal(8 * 5, score.Total);
    }

    [Fact]
    public void SharedTile_CountsInBothWords_WithoutExtraBonusChips()
    {
        // New S at (1,1) forms AS across and AS down: S counted once per word, plus +2 mult.
        var board = BoardFromRows("CAT..", "A....", ".....", ".....", ".....");
        var play = Play(board, "S", 1, 1, Direction.Across, "S");

        var score = Score(play);

        Assert.Equal(2 + 2 + 2, score.Chips);
        Assert.Equal(3m, score.Mult);
        Assert.Equal(18, score.Total);
    }

    [Fact]
    public void DeskItems_ApplyAfterIntersections_InSlotOrder()
    {
        var play = Play(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

        var score = Score(play, new PlusMult("plus", 1), new TimesMult("times", 1.5m));

        Assert.Equal(3m, score.Mult); // (1 + 1) × 1.5
        Assert.Equal(30, score.Total);
    }

    [Fact]
    public void Log_RecordsEachStep_ForUiPlayback()
    {
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var play = Play(board, "TO", 1, 1, Direction.Across, "TO");

        var score = Score(play, new PlusMult("desk-1", 1));

        Assert.Equal(
            [ScoringEngine.Sources.Tier, ScoringEngine.Sources.Word, ScoringEngine.Sources.Word,
             ScoringEngine.Sources.Word, ScoringEngine.Sources.Intersection, "desk-1"],
            score.Log.Select(e => e.SourceId));
        Assert.Equal(score.Chips, score.Log[^1].ChipsAfter);
        Assert.Equal(score.Mult, score.Log[^1].MultAfter);
    }

    [Fact]
    public void BonusMult_AddsAfterIntersections_BeforeDeskItems()
    {
        var board = BoardFromRows("CAT..", ".....", ".....", ".....", ".....");
        var play = Play(board, "TO", 1, 1, Direction.Across, "TO");

        var plain = ScoringEngine.Score(play, [new TimesMult("desk-1", 2)], Config);
        var bonus = ScoringEngine.Score(play, [new TimesMult("desk-1", 2)], Config with { BonusMult = 3 });

        Assert.Equal((plain.Mult / 2 + 3) * 2, bonus.Mult); // the ×2 Desk Item multiplies the bonus too
        Assert.Equal(
            [ScoringEngine.Sources.Intersection, ScoringEngine.Sources.Bonus, "desk-1"],
            bonus.Log.Select(e => e.SourceId).TakeLast(3));
    }

    [Fact]
    public void RoundConfig_BonusMult_StacksOnTheEffectiveScoring()
    {
        var round = new RoundConfig(TargetScore: 1, BonusMult: 3);

        Assert.Equal(0m, new RoundConfig(TargetScore: 1).EffectiveScoring(Config).BonusMult);
        Assert.Equal(3m, round.EffectiveScoring(Config).BonusMult);
        Assert.Equal(5m, round.EffectiveScoring(Config with { BonusMult = 2 }).BonusMult);
    }

    [Fact]
    public void DefaultConfig_TiersAscendAndCoverTwoLetterWords()
    {
        var tiers = ScoringConfig.Default.Tiers;

        Assert.Equal(2, tiers.Min(t => t.MinLength));
        Assert.True(tiers.Zip(tiers.Skip(1)).All(p => p.First.MinLength < p.Second.MinLength
            && p.First.BaseChips <= p.Second.BaseChips && p.First.BaseMult <= p.Second.BaseMult));
        Assert.Equal(26, ScoringConfig.Default.LetterValues.Count);
    }

    [Fact]
    public void UnknownLetterValue_DefaultsToZero()
    {
        var config = Config with { LetterValues = ImmutableDictionary<char, int>.Empty };

        Assert.Equal(0, config.ValueOf(Letter.From('Q')));
    }
}
