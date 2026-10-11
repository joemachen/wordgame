using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Run;

namespace Crossword.Tests.Run;

public class EconomyTests
{
    private static readonly EconomyConfig Config = new(
        StartingMoney: 4, PerUnusedSubmission: 1, OverkillStep: 0.5m, OverkillCap: 3, InterestPer: 5, InterestCap: 5)
    { MinPaycheck = 0 };

    private static readonly RoundKind Daily = new("Daily", 1m, BasePay: 3, IsBoss: false);

    private static RoundState Round(long target, long score, int submissionsLeft, int discardsLeft = 0) =>
        new(new RoundConfig(TargetScore: target), Board.Empty(7), TileBag.Empty, Hand.Empty, Rng.FromSeed(1),
            Score: score, SubmissionsLeft: submissionsLeft, DiscardsLeft: discardsLeft);

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

    [Fact]
    public void UnusedDiscards_PayNothing_UnlessTurnedOn()
    {
        var round = Round(300, 300, submissionsLeft: 0, discardsLeft: 2);

        Assert.Equal(0, Economy.Calculate(Config, Daily, round, 0).UnusedDiscards);

        var payout = Economy.Calculate(Config with { PerUnusedDiscard = 2 }, Daily, round, 0);
        Assert.Equal(4, payout.UnusedDiscards);
        Assert.Equal(7, payout.Total);
    }

    [Theory]
    [InlineData(0, 0, 3)] // off: base pay alone
    [InlineData(6, 3, 6)] // topped up to the floor
    [InlineData(2, 0, 3)] // already above the floor
    public void MinPaycheck_TopsUpTheTotal(int floor, int expectedTopUp, int expectedTotal)
    {
        var payout = Economy.Calculate(Config with { MinPaycheck = floor }, Daily, Round(300, 300, 0), 0);

        Assert.Equal(3, payout.Base);
        Assert.Equal(expectedTopUp, payout.FloorTopUp);
        Assert.Equal(expectedTotal, payout.Total);
    }

    [Fact]
    public void MinPaycheck_CountsEveryComponent()
    {
        // base 3 + 2 unused submissions = 5 < floor 6 → topped up by 1 only.
        var payout = Economy.Calculate(Config with { MinPaycheck = 6 }, Daily, Round(300, 300, submissionsLeft: 2), 0);

        Assert.Equal(new Payout(Base: 3, UnusedSubmissions: 2, Overkill: 0, Interest: 0) { FloorTopUp = 1 }, payout);
        Assert.Equal(6, payout.Total);
    }

    [Fact]
    public void HowToEarnMore_ListsOnlyTheRulesInForce()
    {
        var text = Economy.HowToEarnMore(Config with { OverkillStep = 0.25m, MinPaycheck = 5 });

        Assert.Equal("Earn more: +$1 per unused submission  ·  +$1 per 25% over the deadline (max $3)  ·  "
            + "+$1 interest per $5 held (max $5)  ·  every paycheck is at least $5", text);
        Assert.DoesNotContain("discard", text);
        Assert.DoesNotContain("at least", Economy.HowToEarnMore(Config));
        Assert.Contains("+$2 per unused discard", Economy.HowToEarnMore(Config with { PerUnusedDiscard = 2 }));
    }
}
