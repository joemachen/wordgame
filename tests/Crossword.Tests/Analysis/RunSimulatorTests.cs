using Crossword.Core.Analysis;
using Crossword.Core.Lexicon;
using Crossword.Core.Run;

namespace Crossword.Tests.Analysis;

public class RunSimulatorTests
{
    // A short run keeps the test fast while exercising round → shop → round transitions.
    private static readonly RunConfig ShortRun = RunConfig.Default with { WeekTargets = [100, 200] };

    [Fact]
    public void PlayRun_IsDeterministic()
    {
        var a = RunSimulator.PlayRun(11, ShortRun, LexiconLoader.Enable);
        var b = RunSimulator.PlayRun(11, ShortRun, LexiconLoader.Enable);

        Assert.Equal(a.Rounds, b.Rounds);
        Assert.Equal(a.FinalMoney, b.FinalMoney);
        Assert.Equal(a.DeskItems, b.DeskItems);
    }

    [Fact]
    public void PlayRun_EasyTargets_WinsAllRounds_AndShops()
    {
        var run = RunSimulator.PlayRun(12, ShortRun, LexiconLoader.Enable);

        Assert.True(run.Victory);
        Assert.Equal(ShortRun.TotalRounds, run.RoundsCleared);
        Assert.NotEmpty(run.DeskItems);
        Assert.Contains(run.Rounds, r => r.Boss is not null);
    }

    [Fact]
    public void PlayRun_NaiveShopBot_StillPlaysThrough()
    {
        var a = RunSimulator.PlayRun(12, ShortRun, LexiconLoader.Enable, strategy: ShopStrategy.Naive);
        var b = RunSimulator.PlayRun(12, ShortRun, LexiconLoader.Enable, strategy: ShopStrategy.Naive);

        Assert.True(a.Victory);
        Assert.NotEmpty(a.DeskItems);
        Assert.Equal(a.Rounds, b.Rounds);
    }

    [Fact]
    public void PlayRun_ImpossibleTarget_EndsOnFirstRound()
    {
        var run = RunSimulator.PlayRun(13, RunConfig.Default with { WeekTargets = [1_000_000] }, LexiconLoader.Enable);

        Assert.False(run.Victory);
        Assert.Single(run.Rounds);
        Assert.Equal(0, run.RoundsCleared);
    }
}
