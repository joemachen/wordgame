using System.Collections.Immutable;
using Crossword.Core.Effects;
using Crossword.Core.Random;

namespace Crossword.Core.Domain;

/// <summary>
/// Root immutable state for a single roguelike run. Holding the <see cref="Rng"/> state here
/// means any RunState can be saved, restored, and replayed deterministically.
/// </summary>
public sealed record RunState(
    ulong Seed,
    Rng Rng,
    ImmutableArray<Tile> Deck,
    ImmutableArray<IDeskItem> DeskItems,
    int RoundIndex)
{
    public static RunState New(ulong seed) => new(
        Seed: seed,
        Rng: Rng.FromSeed(seed),
        Deck: StartingDeck.Create(),
        DeskItems: ImmutableArray<IDeskItem>.Empty,
        RoundIndex: 0);

    public RunState AdvanceRound() => this with { RoundIndex = RoundIndex + 1 };
}
