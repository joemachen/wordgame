using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>
/// Root immutable state for a single roguelike run. Holding the <see cref="Rng"/> state here
/// means any RunState can be saved, restored, and replayed deterministically.
/// </summary>
public sealed record RunState(ulong Seed, Rng Rng, TileBag Bag, Hand Hand, int HandSize)
{
    public const int DefaultHandSize = 8;

    public static RunState New(ulong seed, int handSize = DefaultHandSize)
    {
        if (handSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(handSize), handSize, "Hand size must be positive.");

        return new RunState(
            Seed: seed,
            Rng: Rng.FromSeed(seed),
            Bag: new TileBag(StartingDeck.Create()),
            Hand: Hand.Empty,
            HandSize: handSize);
    }
}
