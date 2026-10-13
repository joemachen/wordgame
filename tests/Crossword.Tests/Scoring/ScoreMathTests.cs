using Crossword.Core.Scoring;

namespace Crossword.Tests.Scoring;

/// <summary>Scores compound into the billions and beyond; the arithmetic saturates at the caps instead of throwing or wrapping.</summary>
[Trait("Category", "Scoring")]
public class ScoreMathTests
{
    [Fact]
    public void Total_FloorsChipsTimesMult()
    {
        Assert.Equal(25, ScoreMath.Total(10, 2.5m));
        Assert.Equal(7, ScoreMath.Total(3, 2.5m));
        Assert.Equal(0, ScoreMath.Total(10, 0m));
    }

    [Fact]
    public void Total_IsNeverNegative()
    {
        Assert.Equal(0, ScoreMath.Total(-10, 2m));
        Assert.Equal(0, ScoreMath.Total(10, -2m));
    }

    [Fact]
    public void Total_CapsInsteadOfThrowing()
    {
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Total(long.MaxValue, 1_000_000_000_000_000_000m));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Total(long.MaxValue, decimal.MaxValue));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Total(ScoreMath.MaxScore, 1m));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Total(ScoreMath.MaxScore / 2 + 1, 2m));
        Assert.Equal(ScoreMath.MaxScore - 2, ScoreMath.Total(ScoreMath.MaxScore / 2 - 1, 2m));
    }

    [Fact]
    public void Total_HandlesFractionalMultOnHugeChips()
    {
        Assert.Equal(ScoreMath.MaxScore / 2, ScoreMath.Total(ScoreMath.MaxScore, 0.5m));
        Assert.Equal(400_000_000_000_000_000, ScoreMath.Total(4_000_000_000_000_000_000, 0.1m));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Total(long.MaxValue, 0.25m)); // 2.3e18 is still past the cap
    }

    [Fact]
    public void Add_Saturates()
    {
        Assert.Equal(30, ScoreMath.Add(10, 20));
        Assert.Equal(-10, ScoreMath.Add(10, -20));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Add(ScoreMath.MaxScore, 1));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Add(long.MaxValue, long.MaxValue));
        Assert.Equal(-ScoreMath.MaxScore, ScoreMath.Add(long.MinValue, -1));
        Assert.Equal(ScoreMath.MaxScore - 1, ScoreMath.Add(ScoreMath.MaxScore, -1));
    }

    [Fact]
    public void Scale_FloorsAndSaturates()
    {
        Assert.Equal(25, ScoreMath.Scale(10, 2.5m));
        Assert.Equal(7, ScoreMath.Scale(10, 0.75m));
        Assert.Equal(-20, ScoreMath.Scale(-10, 2m));
        Assert.Equal(0, ScoreMath.Scale(10, 0m));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Scale(ScoreMath.MaxScore / 2 + 1, 2m));
        Assert.Equal(ScoreMath.MaxScore, ScoreMath.Scale(long.MaxValue, decimal.MaxValue));
        Assert.Equal(-ScoreMath.MaxScore, ScoreMath.Scale(long.MinValue, 2m));
    }

    [Fact]
    public void MultArithmetic_Saturates()
    {
        Assert.Equal(5.5m, ScoreMath.AddMult(2.5m, 3m));
        Assert.Equal(7.5m, ScoreMath.MultiplyMult(2.5m, 3m));
        Assert.Equal(0m, ScoreMath.MultiplyMult(2.5m, 0m));
        Assert.Equal(ScoreMath.MaxMult, ScoreMath.AddMult(ScoreMath.MaxMult, 1m));
        Assert.Equal(ScoreMath.MaxMult, ScoreMath.AddMult(decimal.MaxValue, decimal.MaxValue));
        Assert.Equal(ScoreMath.MaxMult, ScoreMath.MultiplyMult(ScoreMath.MaxMult / 2 + 1, 2m));
        Assert.Equal(ScoreMath.MaxMult, ScoreMath.MultiplyMult(decimal.MaxValue, decimal.MaxValue));
    }
}
