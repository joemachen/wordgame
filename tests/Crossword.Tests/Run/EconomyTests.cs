using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Run;

namespace Crossword.Tests.Run;

public class EconomyTests
{
    private static readonly EconomyConfig Config = new(
        StartingMoney: 4, PerUnusedSubmission: 1, OverkillStep: 0.5m, OverkillCap: 3, InterestPer: 5, InterestCap: 5);

    private static readonly RoundKind Daily = new("Daily", 1m, BasePay: 3, IsBoss: false);

    private static RoundState Round(long target, long score, int submissionsLeft) =>
        new(new RoundConfig(TargetScore: target), Board.Empty(7), TileBag.Empty, Hand.Empty, Rng.FromSeed(1),
            Score: score, SubmissionsLeft: submissionsLeft, DiscardsLeft: 0);

    [Fact]
    public void Payout_IncludesBasePay_AndUnusedSubmissions()
    {
        var payout = Economy.Calculate(Config, Daily, Round(300, 300, submissionsLeft: 2), moneyBeforePayout: 0);

        Assert.Equal(new Payout(Base: 3, UnusedSubmissions: 2, Overkill: 0, Interest: 0), payout);
        Assert.Equal(5, payout.Total);
    }

    [Theory]
    [InlineData(300, 0)] // exactly on target
    [InlineData(449, 0)] // just under +50%
    [InlineData(450, 1)] // +50%
    [InlineData(600, 2)] // +100%
    [InlineData(5000, 3)] // capped
    public void Overkill_PaysPerFullStep_UpToCap(long score, int expected)
    {
        Assert.Equal(expected, Economy.Calculate(Config, Daily, Round(300, score, 0), 0).Overkill);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(23, 4)]
    [InlineData(100, 5)] // capped
    public void Interest_IsOnePerFiveHeld_UpToCap(int money, int expected)
    {
        Assert.Equal(expected, Economy.Calculate(Config, Daily, Round(300, 300, 0), money).Interest);
    }
}
