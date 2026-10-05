using System.Collections.Immutable;
using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>Number of symmetric premium-square PAIRS to place on a board.</summary>
public sealed record PremiumPairs(int TripleWord, int DoubleWord, int TripleLetter, int DoubleLetter)
{
    public static PremiumPairs Default { get; } = new(TripleWord: 1, DoubleWord: 2, TripleLetter: 1, DoubleLetter: 2);

    public int Total => TripleWord + DoubleWord + TripleLetter + DoubleLetter;
}

public static class PremiumLayout
{
    /// <summary>
    /// Random premium layout with crossword-style 180° rotational symmetry: every premium at (r, c)
    /// is mirrored at (n-1-r, n-1-c). The centre cell of odd boards is left plain.
    /// </summary>
    public static (ImmutableArray<Premium> Layout, Rng Next) Generate(int size, PremiumPairs pairs, Rng rng)
    {
        int cellCount = size * size;
        // Each canonical index i pairs with its mirror (cellCount - 1 - i); i < mirror excludes the centre.
        var canonical = Enumerable.Range(0, cellCount).Where(i => i < cellCount - 1 - i).ToList();
        if (pairs.Total > canonical.Count)
            throw new ArgumentException($"A {size}x{size} board fits at most {canonical.Count} premium pairs.", nameof(pairs));

        var (shuffled, next) = rng.Shuffle(canonical);
        var kinds = Enumerable.Repeat(Premium.TripleWord, pairs.TripleWord)
            .Concat(Enumerable.Repeat(Premium.DoubleWord, pairs.DoubleWord))
            .Concat(Enumerable.Repeat(Premium.TripleLetter, pairs.TripleLetter))
            .Concat(Enumerable.Repeat(Premium.DoubleLetter, pairs.DoubleLetter));

        var layout = new Premium[cellCount];
        int k = 0;
        foreach (var kind in kinds)
        {
            int index = shuffled[k++];
            layout[index] = kind;
            layout[cellCount - 1 - index] = kind;
        }
        return (ImmutableArray.Create(layout), next);
    }

    /// <summary>
    /// Seeded, rotationally symmetric blocked cells ("black squares"), placed only on cells without premiums.
    /// </summary>
    public static (ImmutableHashSet<Position> Blocked, Rng Next) GenerateBlocked(
        int size, int pairs, ImmutableArray<Premium> premiums, Rng rng)
    {
        int cellCount = size * size;
        var candidates = Enumerable.Range(0, cellCount)
            .Where(i => i < cellCount - 1 - i && premiums[i] == Premium.None && premiums[cellCount - 1 - i] == Premium.None)
            .ToList();
        if (pairs > candidates.Count)
            throw new ArgumentException($"Only {candidates.Count} blocked pairs fit on this board.", nameof(pairs));

        var (shuffled, next) = rng.Shuffle(candidates);
        var blocked = ImmutableHashSet.CreateBuilder<Position>();
        foreach (int index in shuffled.Take(pairs))
        {
            blocked.Add(new Position(index / size, index % size));
            int mirror = cellCount - 1 - index;
            blocked.Add(new Position(mirror / size, mirror % size));
        }
        return (blocked.ToImmutable(), next);
    }
}
