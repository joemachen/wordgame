using Crossword.Core.Run;

namespace Crossword.Tests.Run;

public class RunProgressTests
{
    private static readonly RunConfig Config = RunConfig.Default with
    {
        WeekTargets = [100, 200, 300],
        Days =
        [
            new RoundKind("Daily", 1, 3, IsBoss: false),
            new RoundKind("Saturday Stumper", 1.3m, 4, IsBoss: false),
            new RoundKind("Sunday Edition", 1.6m, 5, IsBoss: true),
        ],
    };

    [Theory]
    [InlineData(0, 0, 0, 2)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(2, 0, 2, 0)]
    [InlineData(4, 1, 1, 1)]
    public void For_PlacesTheRoundInItsWeek(int roundIndex, int week, int day, int untilBoss)
    {
        var progress = RunProgress.For(Config, roundIndex);

        Assert.Equal(week, progress.Week);
        Assert.Equal(day, progress.Day);
        Assert.Equal(2, progress.BossDay);
        Assert.Equal(untilBoss, progress.PuzzlesUntilBoss);
        Assert.Equal(untilBoss == 0, progress.IsBossDay);
        Assert.False(progress.Endless);
    }

    [Fact]
    public void For_PastTheLastWeek_IsEndless()
    {
        var progress = RunProgress.For(Config, 9);

        Assert.Equal(3, progress.Week);
        Assert.True(progress.Endless);
    }
}
