using System.Collections.Immutable;
using Crossword.Core.Effects;
using Crossword.Core.Random;
using Crossword.Core.Stationery;

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

    /// <summary>Desk Item slots of a run without upgrades (<see cref="Run.RunConfig.DeskSlots"/>).</summary>
    public const int DefaultDeskSlots = 5;

    /// <summary>The most Desk Item slots a run can reach (Filing Cabinets).</summary>
    public const int MaxDeskSlots = 7;

    /// <summary>Stationery slots of a run without upgrades (<see cref="Run.RunConfig.StationerySlots"/>).</summary>
    public const int DefaultStationerySlots = 2;

    /// <summary>The most Stationery slots a run can reach (Supply Closets).</summary>
    public const int MaxStationerySlots = 4;

    /// <summary>One-shot Stationery held for later use; duplicates are allowed.</summary>
    public ImmutableArray<IStationery> Stationery { get; init; } = ImmutableArray<IStationery>.Empty;

    /// <summary>The run's difficulty level (1 = Proofreader … 8; see <see cref="Run.PressRuns"/>).</summary>
    public int PressRun { get; init; } = 1;

    /// <summary>The run's starting deck (see <see cref="Run.Decks"/>).</summary>
    public string DeckId { get; init; } = "standard";

    /// <summary>
    /// Dictionary overlays whose words are legal this run (see <see cref="Lexicon.Dictionaries"/>; empty = ENABLE only).
    /// The run's word graph is <c>LexiconLoader.For(Dictionaries)</c>.
    /// </summary>
    public ImmutableArray<string> Dictionaries { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// The player chose the seed (a seed box or <c>--seed</c>). Such a run is replayable, so its win unlocks nothing
    /// (<see cref="Profile.StatsRules.RecordRunEnd"/>); play is unchanged.
    /// </summary>
    public bool Seeded { get; init; }

    /// <summary>Office upgrades bought: upgrade id → level (see <see cref="Run.OfficeUpgrades"/>).</summary>
    public ImmutableDictionary<string, int> Upgrades { get; init; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>Style Guide upgrades bought: word tier MinLength → number of upgrades.</summary>
    public ImmutableDictionary<int, int> TierUpgrades { get; init; } = ImmutableDictionary<int, int>.Empty;

    public RunState UpgradeTier(int minLength) =>
        this with { TierUpgrades = TierUpgrades.SetItem(minLength, TierUpgrades.GetValueOrDefault(minLength) + 1) };

    public RunState AdvanceRound() => this with { RoundIndex = RoundIndex + 1 };

    /// <summary>
    /// Adds an item to the desk of <paramref name="slots"/> slots (the run's <see cref="Run.RunConfig.DeskSlots"/>), which
    /// keeps itself in the best order. Duplicates (same Id) need <paramref name="allowDuplicates"/> (the Photocopier).
    /// </summary>
    public Result<RunState, string> AddDeskItem(IDeskItem item, int slots = DefaultDeskSlots, bool allowDuplicates = false)
    {
        if (DeskItems.Length >= slots)
            return Result<RunState, string>.Fail($"All {slots} desk slots are full.");
        if (!allowDuplicates && DeskItems.Any(d => d.Id == item.Id))
            return Result<RunState, string>.Fail($"You already have {item.Name}.");
        return Result<RunState, string>.Ok(this with { DeskItems = DeskOrder.Sorted(DeskItems.Add(item)) });
    }

    public Result<RunState, string> RemoveDeskItem(int slot) =>
        IsSlot(slot)
            ? Result<RunState, string>.Ok(this with { DeskItems = DeskItems.RemoveAt(slot) })
            : Result<RunState, string>.Fail($"No desk item in slot {slot + 1}.");

    /// <summary>Holds <paramref name="item"/> in one of <paramref name="slots"/> slots (the run's <see cref="Run.RunConfig.StationerySlots"/>).</summary>
    public Result<RunState, string> AddStationery(IStationery item, int slots = DefaultStationerySlots) =>
        Stationery.Length >= slots
            ? Result<RunState, string>.Fail($"All {slots} stationery slots are full.")
            : Result<RunState, string>.Ok(this with { Stationery = Stationery.Add(item) });

    public Result<RunState, string> RemoveStationery(int slot) =>
        slot >= 0 && slot < Stationery.Length
            ? Result<RunState, string>.Ok(this with { Stationery = Stationery.RemoveAt(slot) })
            : Result<RunState, string>.Fail($"No stationery in slot {slot + 1}.");

    private bool IsSlot(int slot) => slot >= 0 && slot < DeskItems.Length;
}
