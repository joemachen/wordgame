using System.Collections.Immutable;

namespace Crossword.Core.Domain;

/// <summary>
/// The default starting tile set: 98 lettered tiles with 41 vowels (~42%, close to Scrabble's share; was 38 before
/// 2026-10-05, which dealt a hand with at most one vowel 16.5% of the time) plus <see cref="Wilds"/> wild tiles, like
/// Scrabble's blanks. Loosely based on English letter frequency.
/// </summary>
public static class StartingDeck
{
    private static readonly (char Letter, int Count)[] Distribution =
    [
        ('A', 9), ('B', 2), ('C', 3), ('D', 4), ('E', 11), ('F', 2), ('G', 2), ('H', 3),
        ('I', 9), ('J', 1), ('K', 1), ('L', 4), ('M', 3), ('N', 6), ('O', 8), ('P', 2),
        ('Q', 1), ('R', 6), ('S', 5), ('T', 6), ('U', 4), ('V', 1), ('W', 1), ('X', 1),
        ('Y', 2), ('Z', 1),
    ];

    /// <summary>
    /// The Redactor Deck's tiles: 29 common letters (12 vowels, ~41%; no Q, Z, X, J, K, V, W, Y or F) plus one wild = 30,
    /// the shop's minimum deck size.
    /// </summary>
    private static readonly (char Letter, int Count)[] ThinDistribution =
    [
        ('A', 3), ('B', 1), ('C', 1), ('D', 1), ('E', 4), ('G', 1), ('H', 1), ('I', 2), ('L', 2), ('M', 1),
        ('N', 2), ('O', 2), ('P', 1), ('R', 2), ('S', 2), ('T', 2), ('U', 1),
    ];

    public const int Wilds = 2;
    public const int ThinWilds = 1;

    public static ImmutableArray<Tile> Create() => Build(Distribution, Wilds);

    /// <summary>The Redactor Deck's thin 30-tile set (see <see cref="Run.Decks"/>).</summary>
    public static ImmutableArray<Tile> Thin() => Build(ThinDistribution, ThinWilds);

    private static ImmutableArray<Tile> Build((char Letter, int Count)[] distribution, int wilds)
    {
        var builder = ImmutableArray.CreateBuilder<Tile>();
        int nextId = 0;
        foreach (var (letter, count) in distribution)
        {
            for (int i = 0; i < count; i++)
                builder.Add(new Tile(nextId++, Letter.From(letter)));
        }
        for (int i = 0; i < wilds; i++)
            builder.Add(Tile.Wild(nextId++));
        return builder.ToImmutable();
    }
}
