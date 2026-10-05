using Crossword.Core.Random;

namespace Crossword.Tests.Random;

[Trait("Category", "Determinism")]
public class RngTests
{
    private static ulong[] Sequence(Rng rng, int length)
    {
        var values = new ulong[length];
        for (int i = 0; i < length; i++)
            (values[i], rng) = rng.NextUInt64();
        return values;
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        Assert.Equal(Sequence(Rng.FromSeed(42), 100), Sequence(Rng.FromSeed(42), 100));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        Assert.NotEqual(Sequence(Rng.FromSeed(1), 10), Sequence(Rng.FromSeed(2), 10));
    }

    [Fact]
    public void NextUInt64_DoesNotMutateOriginal()
    {
        var rng = Rng.FromSeed(7);
        var (first, _) = rng.NextUInt64();
        var (again, _) = rng.NextUInt64();
        Assert.Equal(first, again);
    }

    [Fact]
    public void NextInt_StaysWithinBounds_AndCoversRange()
    {
        var rng = Rng.FromSeed(123);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            (int value, rng) = rng.NextInt(6);
            Assert.InRange(value, 0, 5);
            seen.Add(value);
        }
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void Shuffle_IsDeterministicPermutation()
    {
        var items = Enumerable.Range(0, 50).ToArray();

        var (a, _) = Rng.FromSeed(8).Shuffle(items);
        var (b, _) = Rng.FromSeed(8).Shuffle(items);

        Assert.Equal(a, b);
        Assert.NotEqual(items, a.ToArray());
        Assert.Equal(items, a.Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_RejectsNonPositiveBound(int bound)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rng.FromSeed(1).NextInt(bound));
    }
}
