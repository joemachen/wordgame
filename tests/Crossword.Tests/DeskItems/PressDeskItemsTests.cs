using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

[Trait("Category", "Scoring")]
public class PressDeskItemsTests
{
    /// <summary>Fixed numbers so retuning <see cref="ScoringConfig.Default"/> never breaks these tests.</summary>
    private static readonly ScoringConfig Config = ScoringConfig.Default with
    {
        Tiers = [new WordTier(2, 2, 1), new WordTier(3, 5, 1), new WordTier(4, 10, 2)],
        LetterValues = ScoringConfig.Default.LetterValues.SetItems(
            [new('C', 3), new('A', 1), new('T', 1), new('O', 1), new('S', 1), new('R', 1)]),
        IntersectionMult = 2,
    };

    // CAT across on an empty board: 1 word, 3 + 1 + 1 = 5 letter chips.
    private static readonly PlayAnalysis SimplePlay = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

    // TO under AT in CAT: words TO, AT, TO — 3 words.
    private static readonly PlayAnalysis ParallelPlay =
        PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");

    private static ScoreContext Start(PlayAnalysis play, ScoreEnvironment? env = null) =>
        ScoreContext.Start(play, chips: 10, mult: 2) with { Env = env ?? ScoreEnvironment.Empty };

    private static void AssertNoEffect(IDeskItem item, ScoreContext start) => Assert.Same(start, item.Apply(start));

    [Fact]
    public void EtymologyTome_CountsTheLongestWordsLetterChipsTwice()
    {
        var plain = ScoringEngine.Score(SimplePlay, [], Config);
        var tome = ScoringEngine.Score(SimplePlay, [new EtymologyTome()], Config);

        Assert.Equal(5 + 5, plain.Chips);
        Assert.Equal(5 + 5 + 5, tome.Chips);
        Assert.Contains(tome.Log, e => e.SourceId == "etymology-tome" && e.Description.Contains("+5 chips"));
    }

    [Fact]
    public void EtymologyTome_DoesNothing_WhenTheLongestWordScoredNoLetterChips()
    {
        var env = ScoreEnvironment.Empty with { WordsFormed = ["CAT"] };
        var repeated = ScoringEngine.Score(SimplePlay, [new EtymologyTome()], Config with { RepeatWordsScoreZero = true }, env);

        Assert.Equal(5, repeated.Chips);
        Assert.DoesNotContain(repeated.Log, e => e.SourceId == "etymology-tome");
        AssertNoEffect(new EtymologyTome(), Start(SimplePlay)); // no word chips recorded
    }

    [Fact]
    public void RubberStamp_MultipliesOnlyTheRoundsFirstSubmission()
    {
        Assert.Equal(4m, new RubberStamp(Factor: 2).Apply(Start(SimplePlay)).Mult);
        AssertNoEffect(new RubberStamp(Factor: 2), Start(SimplePlay, ScoreEnvironment.Empty with { SubmissionsMade = 1 }));
    }

    [Fact]
    public void PrintingPressRoller_GrowsOnPlaysFormingManyWords()
    {
        IDeskItem roller = new PrintingPressRoller(MinWords: 3, Gain: 0.1m, Factor: 1);

        AssertNoEffect(roller, Start(SimplePlay));
        Assert.Same(roller, roller.AfterPlay(SimplePlay));
        var grown = (PrintingPressRoller)roller.AfterPlay(ParallelPlay);
        Assert.Equal(1.1m, grown.Factor);
        Assert.Equal(3m, new PrintingPressRoller(Factor: 1.5m).Apply(Start(SimplePlay)).Mult);
    }

    [Fact]
    public void TileRack_DealsOneMoreTile()
    {
        var config = new TileRack(ExtraTiles: 1).ModifyRound(new RoundConfig(TargetScore: 500));
        var (round, _) = RoundRules.Start(RunState.New(5), config, LexiconLoader.Enable);

        Assert.Equal(8, config.HandSize);
        Assert.Equal(8, round.Hand.Count);
    }

    [Fact]
    public void CoffeeStain_AddsMult_AndStainsAMirroredPairOffPremiums()
    {
        var item = new CoffeeStain(Mult: 4, StainedPairs: 1);
        var (round, _) = RoundRules.Start(RunState.New(5), item.ModifyRound(new RoundConfig(TargetScore: 500)), LexiconLoader.Enable);
        var board = round.Board;

        Assert.Equal(6m, item.Apply(Start(SimplePlay)).Mult);
        Assert.Equal(2, board.Blocked.Count);
        Assert.All(board.Blocked, p =>
        {
            Assert.Contains(new Position(board.Size - 1 - p.Row, board.Size - 1 - p.Col), board.Blocked);
            Assert.Equal(Premium.None, board.PremiumAt(p));
        });
    }

    [Fact]
    public void BlockedPairs_BeyondTheFreeSquares_BlockEveryFreePair()
    {
        // 5×5 has 12 mirrored pairs; Tight Margins' premiums take 4, so at most 8 can be blocked.
        var config = new TightMargins().Apply(new RoundConfig(TargetScore: 500)) with { BlockedPairs = 20 };
        var (round, _) = RoundRules.Start(RunState.New(5), config, LexiconLoader.Enable);

        Assert.Equal(16, round.Board.Blocked.Count);
        Assert.All(round.Board.Blocked, p => Assert.Equal(Premium.None, round.Board.PremiumAt(p)));
    }

    [Fact]
    public void RunRules_AppliesDeskItemRoundHooks_WhenARoundStarts_WithoutChangingTheDeadline()
    {
        var session = RunRules.NewGame(3, RunConfig.Default, LexiconLoader.Enable);
        var run = session.Run.AddDeskItem(new TileRack(1)).Value.AddDeskItem(new CoffeeStain(4, 1)).Value;
        var shop = session with { Run = run, Phase = RunPhase.Shop };

        var next = RunRules.LeaveShop(shop, LexiconLoader.Enable).Value;

        Assert.Equal(8, next.Round.Hand.Count);
        Assert.Equal(2, next.Round.Board.Blocked.Count);
        Assert.Equal(RunRules.TargetFor(next.Config, next.Run, next.Run.RoundIndex), next.Round.Config.TargetScore);
    }
}
