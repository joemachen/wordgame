using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Analysis;

public class RoundSimulatorTests
{
    private static SimulatedRound Play(ulong seed, double skill = 1.0) =>
        RoundSimulator.PlayRound(seed, RunConfig.Default.RoundConfigFor(0), LexiconLoader.Enable, Array.Empty<IDeskItem>(), ScoringConfig.Default, skill);

    [Fact]
    public void PlayRound_UsesEverySubmission_AndIsDeterministic()
    {
        var a = Play(3);
        var b = Play(3);

        Assert.Equal(RunConfig.Default.RoundConfigFor(0).Submissions, a.Plays.Length);
        Assert.Equal(a.FinalScore, b.FinalScore);
        Assert.Equal(a.Plays.Sum(p => p.Score), a.FinalScore);
    }

    [Fact]
    public void GreedyPlay_OutscoresLowerSkill()
    {
        Assert.True(Play(4).FinalScore > Play(4, skill: 0.5).FinalScore);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void PlayRound_RejectsInvalidSkill(double skill)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Play(1, skill));
    }
}
