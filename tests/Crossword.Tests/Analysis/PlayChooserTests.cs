using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Analysis;

[Trait("Category", "Analysis")]
public class PlayChooserTests
{
    private static IReadOnlyList<RankedPlay> Ranked() =>
        MoveRanker.Rank(Board.Empty(7), HandOf("STAREDL"), LexiconLoader.Enable, Array.Empty<IDeskItem>(), ScoringConfig.Default);

    [Theory]
    [InlineData(SkillModel.Percentile)]
    [InlineData(SkillModel.ScoreFraction)]
    public void FullSkill_PicksTheBestPlay(SkillModel model)
    {
        var ranked = Ranked();

        Assert.Same(ranked[0], PlayChooser.Choose(ranked, 1.0, model));
    }

    [Fact]
    public void Percentile_PicksByRankPosition()
    {
        var ranked = Ranked();

        Assert.Same(ranked[(int)(0.5 * (ranked.Count - 1))], PlayChooser.Choose(ranked, 0.5));
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.6)]
    [InlineData(0.3)]
    public void ScoreFraction_PicksTheBestPlayWithinTheFraction(double skill)
    {
        var ranked = Ranked();
        long cap = (long)Math.Floor(ranked[0].Score.Total * skill);

        var chosen = PlayChooser.Choose(ranked, skill, SkillModel.ScoreFraction);

        Assert.True(chosen.Score.Total <= cap);
        Assert.DoesNotContain(ranked, r => r.Score.Total <= cap && r.Score.Total > chosen.Score.Total);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void RejectsInvalidSkill(double skill)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayChooser.Choose(Ranked(), skill));
    }
}
