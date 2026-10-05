using Crossword.Core.Domain;
using Crossword.Core.Random;

namespace Crossword.Tests.Domain;

public class PremiumLayoutTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Generate_IsRotationallySymmetric(int size)
    {
        var (layout, _) = PremiumLayout.Generate(size, PremiumPairs.Default, Rng.FromSeed(3));

        for (int i = 0; i < layout.Length; i++)
            Assert.Equal(layout[i], layout[layout.Length - 1 - i]);
    }

    [Fact]
    public void Generate_PlacesRequestedCounts_AndLeavesCentrePlain()
    {
        var pairs = new PremiumPairs(TripleWord: 1, DoubleWord: 2, TripleLetter: 3, DoubleLetter: 4);

        var (layout, _) = PremiumLayout.Generate(7, pairs, Rng.FromSeed(3));

        Assert.Equal(2, layout.Count(p => p == Premium.TripleWord));
        Assert.Equal(4, layout.Count(p => p == Premium.DoubleWord));
        Assert.Equal(6, layout.Count(p => p == Premium.TripleLetter));
        Assert.Equal(8, layout.Count(p => p == Premium.DoubleLetter));
        Assert.Equal(Premium.None, layout[24]);
    }

    [Fact]
    public void Generate_IsDeterministicPerSeed()
    {
        var (a, _) = PremiumLayout.Generate(7, PremiumPairs.Default, Rng.FromSeed(10));
        var (b, _) = PremiumLayout.Generate(7, PremiumPairs.Default, Rng.FromSeed(10));
        var (c, _) = PremiumLayout.Generate(7, PremiumPairs.Default, Rng.FromSeed(11));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Generate_TooManyPairs_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PremiumLayout.Generate(3, new PremiumPairs(5, 0, 0, 0), Rng.FromSeed(1)));
    }
}
