using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
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
}
