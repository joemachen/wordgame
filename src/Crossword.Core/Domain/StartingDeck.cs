using System.Collections.Immutable;

namespace Crossword.Core.Domain;

/// <summary>
/// The default starting tile set. PLACEHOLDER distribution loosely based on English letter
/// frequency — to be tuned during game design.
/// </summary>
public static class StartingDeck
{
    private static readonly (char Letter, int Count)[] Distribution =
    [
        ('A', 8), ('B', 2), ('C', 3), ('D', 4), ('E', 11), ('F', 2), ('G', 3), ('H', 3),
        ('I', 8), ('J', 1), ('K', 1), ('L', 4), ('M', 3), ('N', 6), ('O', 7), ('P', 2),
        ('Q', 1), ('R', 6), ('S', 5), ('T', 6), ('U', 4), ('V', 2), ('W', 2), ('X', 1),
        ('Y', 2), ('Z', 1),
    ];

    public static ImmutableArray<Tile> Create()
    {
        var builder = ImmutableArray.CreateBuilder<Tile>();
        int nextId = 0;
        foreach (var (letter, count) in Distribution)
        {
            for (int i = 0; i < count; i++)
                builder.Add(new Tile(nextId++, Letter.From(letter)));
        }
        return builder.ToImmutable();
    }
}
