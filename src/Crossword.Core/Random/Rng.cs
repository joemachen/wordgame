namespace Crossword.Core.Random;

/// <summary>
/// Immutable, seedable pseudo-random generator (SplitMix64).
/// Every call returns the value AND the next generator state, so transitions that
/// consume randomness stay pure and runs are fully reproducible from a seed.
/// All randomness in Core MUST flow through this type — never System.Random or engine RNG.
/// </summary>
public readonly record struct Rng(ulong State)
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    public static Rng FromSeed(ulong seed) => new(seed);

    public (ulong Value, Rng Next) NextUInt64()
    {
        ulong state = State + GoldenGamma;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (z ^ (z >> 31), new Rng(state));
    }

    /// <summary>Returns a uniformly distributed integer in [0, maxExclusive).</summary>
    public (int Value, Rng Next) NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");

        // Rejection sampling removes modulo bias.
        ulong bound = (ulong)maxExclusive;
        ulong limit = ulong.MaxValue - (ulong.MaxValue % bound);
        var rng = this;
        while (true)
        {
            (ulong raw, rng) = rng.NextUInt64();
            if (raw < limit)
                return ((int)(raw % bound), rng);
        }
    }
}
