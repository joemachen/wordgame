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
    int RoundIndex,
    int Money = 0)
{
    public static RunState New(ulong seed) => new(
        Seed: seed,
        Rng: Rng.FromSeed(seed),
        Deck: StartingDeck.Create(),
        DeskItems: ImmutableArray<IDeskItem>.Empty,
        RoundIndex: 0);

    public const int MaxDeskSlots = 5;

    public RunState AdvanceRound() => this with { RoundIndex = RoundIndex + 1 };

    /// <summary>Adds an item to the rightmost slot. Duplicates (same Id) are not allowed.</summary>
    public Result<RunState, string> AddDeskItem(IDeskItem item)
    {
        if (DeskItems.Length >= MaxDeskSlots)
            return Result<RunState, string>.Fail($"All {MaxDeskSlots} desk slots are full.");
        if (DeskItems.Any(d => d.Id == item.Id))
            return Result<RunState, string>.Fail($"You already have {item.Name}.");
        return Result<RunState, string>.Ok(this with { DeskItems = DeskItems.Add(item) });
    }

    public Result<RunState, string> RemoveDeskItem(int slot) =>
        IsSlot(slot)
            ? Result<RunState, string>.Ok(this with { DeskItems = DeskItems.RemoveAt(slot) })
            : Result<RunState, string>.Fail($"No desk item in slot {slot + 1}.");

    /// <summary>Reorders items; slot order is the order effects apply in.</summary>
    public Result<RunState, string> MoveDeskItem(int from, int to)
    {
        if (!IsSlot(from) || !IsSlot(to))
            return Result<RunState, string>.Fail("Both slots must hold desk items.");
        var item = DeskItems[from];
        return Result<RunState, string>.Ok(this with { DeskItems = DeskItems.RemoveAt(from).Insert(to, item) });
    }

    private bool IsSlot(int slot) => slot >= 0 && slot < DeskItems.Length;
}
