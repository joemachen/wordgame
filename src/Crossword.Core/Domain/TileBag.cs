using System.Collections.Immutable;
using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>The undrawn tiles for the current round. Immutable; draws return a new bag.</summary>
public sealed record TileBag(ImmutableArray<Tile> Tiles)
{
    public static TileBag Empty { get; } = new(ImmutableArray<Tile>.Empty);

    public int Count => Tiles.Length;

    public bool IsEmpty => Tiles.IsEmpty;

    /// <summary>
    /// Draws up to <paramref name="count"/> tiles at random. If the bag holds fewer,
    /// all remaining tiles are drawn. Returns the drawn tiles, the remaining bag, and the advanced RNG.
    /// </summary>
    public (ImmutableArray<Tile> Drawn, TileBag Remaining, Rng Rng) Draw(int count, Rng rng)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot draw a negative number of tiles.");

        var remaining = Tiles.ToBuilder();
        var drawn = ImmutableArray.CreateBuilder<Tile>(Math.Min(count, remaining.Count));

        while (drawn.Count < count && remaining.Count > 0)
        {
            (int index, rng) = rng.NextInt(remaining.Count);
            drawn.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return (drawn.MoveToImmutable(), new TileBag(remaining.ToImmutable()), rng);
    }

    /// <summary>
    /// Draws up to <paramref name="count"/> tiles one at a time, each uniformly from the tiles <paramref name="config"/>
    /// allows given <paramref name="hand"/> plus the tiles drawn so far: vowels already held
    /// <see cref="DrawConfig.MaxCopiesPerVowel"/> times are skipped, and when the slots left are only just enough to
    /// reach <see cref="DrawConfig.MinVowels"/> (or <see cref="DrawConfig.MinConsonants"/>), only that class is
    /// eligible. If no tile qualifies, any remaining tile can be drawn.
    /// </summary>
    public (ImmutableArray<Tile> Drawn, TileBag Remaining, Rng Rng) DrawBalanced(int count, IReadOnlyList<Tile> hand,
        DrawConfig config, Rng rng)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot draw a negative number of tiles.");

        var remaining = Tiles.ToList();
        var held = hand.ToList();
        var drawn = ImmutableArray.CreateBuilder<Tile>(Math.Min(count, remaining.Count));

        while (drawn.Count < count && remaining.Count > 0)
        {
            int slotsLeft = count - drawn.Count;
            int vowels = held.Count(t => DrawConfig.IsVowel(t.Letter));
            int needVowels = Math.Max(0, config.MinVowels - vowels);
            int needConsonants = Math.Max(0, config.MinConsonants - (held.Count - vowels));

            var candidates = remaining
                .Where(t => config.MaxCopiesPerVowel <= 0 || !DrawConfig.IsVowel(t.Letter)
                    || held.Count(h => h.Letter == t.Letter) < config.MaxCopiesPerVowel)
                .ToList();
            if (needVowels >= slotsLeft && candidates.Any(t => DrawConfig.IsVowel(t.Letter)))
                candidates = candidates.Where(t => DrawConfig.IsVowel(t.Letter)).ToList();
            else if (needConsonants >= slotsLeft && candidates.Any(t => !DrawConfig.IsVowel(t.Letter)))
                candidates = candidates.Where(t => !DrawConfig.IsVowel(t.Letter)).ToList();
            if (candidates.Count == 0)
                candidates = remaining;

            (int index, rng) = rng.NextInt(candidates.Count);
            var tile = candidates[index];
            drawn.Add(tile);
            held.Add(tile);
            remaining.Remove(tile);
        }

        return (drawn.MoveToImmutable(), new TileBag(remaining.ToImmutableArray()), rng);
    }
}
