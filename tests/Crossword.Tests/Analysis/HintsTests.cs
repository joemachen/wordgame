using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Analysis;

[Trait("Category", "Analysis")]
public class HintsTests
{
    private static readonly Board Start = Board.Empty(7);
    private static readonly Hand Hand = HandOf("STAREDL");

    private static IReadOnlyList<RankedPlay> Ranked() =>
        MoveRanker.Rank(Start, Hand, LexiconLoader.Enable, Array.Empty<IDeskItem>(), ScoringConfig.Default);

    [Fact]
    public void Decent_IsLegal_AndWorthAtMostTheCappedFractionOfTheBest()
    {
        var ranked = Ranked();
        var config = new HintConfig(Percentile: 0.95, MaxFractionOfBest: 0.5);

        var hint = Hints.Decent(ranked, config)!;

        Assert.True(hint.Score.Total <= ranked[0].Score.Total * 0.5);
        Assert.True(PlacementValidator.Validate(Start, Hand, hint.Play.Placed, LexiconLoader.Enable).IsOk);
    }

    [Fact]
    public void Decent_UsesThePercentile_WhenItScoresLowerThanTheCap()
    {
        var ranked = Ranked();
        var config = new HintConfig(Percentile: 0.5, MaxFractionOfBest: 1.0);

        Assert.Same(ranked[(int)(0.5 * (ranked.Count - 1))], Hints.Decent(ranked, config));
    }

    [Fact]
    public void Decent_IsDeterministic_AndNotTheBestPlay()
    {
        var ranked = Ranked();
        var a = Hints.Decent(ranked)!;
        var b = Hints.Decent(Ranked())!;

        Assert.Equal(a.Play.Placed.ToArray(), b.Play.Placed.ToArray());
        Assert.True(a.Score.Total < ranked[0].Score.Total);
    }

    [Fact]
    public void Best_IsTheTopRankedPlay()
    {
        var ranked = Ranked();

        Assert.Same(ranked[0], Hints.Best(ranked));
    }

    [Fact]
    public void NoLegalPlay_GivesNoHint()
    {
        Assert.Null(Hints.Decent(Array.Empty<RankedPlay>()));
        Assert.Null(Hints.Best(Array.Empty<RankedPlay>()));
    }
}
