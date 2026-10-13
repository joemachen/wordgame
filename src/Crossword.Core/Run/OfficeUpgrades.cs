using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Run;

/// <summary>
/// The numbers behind each Office upgrade (ROADMAP §3b phase 3, "cap breakers"). Level N of an upgrade is the Nth
/// purchase; an upgrade is offered until it reaches its last level. Lists hold one value per level.
/// </summary>
public sealed record OfficeConfig(
    int DeskSlotLevels = 2,
    int StationerySlotLevels = 2,
    int HandSizeLevels = 2,
    int SubmissionLevels = 1,
    int DiscardLevels = 2,
    int BroadsheetSize = 9)
{
    /// <summary>The 9×9 board's premium pairs (~25% of squares, like the 7×7 default's 12 of 49).</summary>
    public PremiumPairs BroadsheetPremiums { get; init; } = new(TripleWord: 2, DoubleWord: 3, TripleLetter: 2, DoubleLetter: 3);

    /// <summary>Editorial Cuts: the deck's minimum size at each level.</summary>
    public ImmutableArray<int> DeckMinimums { get; init; } = [20, 15];

    /// <summary>Savings Bond: the interest cap at each level.</summary>
    public ImmutableArray<int> InterestCaps { get; init; } = [10, 20];

    /// <summary>Bonus Clause: the overkill cap at each level.</summary>
    public ImmutableArray<int> OverkillCaps { get; init; } = [6, 12];

    public static OfficeConfig Default { get; } = new();
}

/// <summary>One Office upgrade: a permanent, run-long change to the run's rules, bought in the shop.</summary>
public sealed record OfficeUpgrade(string Id, string Name, int MaxLevel);

/// <summary>
/// Office upgrades break the run's caps: more Desk Item and Stationery slots, a bigger hand, more submissions and
/// discards, a 9×9 board, a smaller minimum deck, duplicate Desk Items and higher interest and overkill caps. Like a
/// deck or a Press Run they are a <see cref="RunConfig"/> transform: the run stores the levels bought
/// (<see cref="RunState.Upgrades"/>) and <see cref="RunRules.ConfigFor(RunConfig, RunState)"/> applies them last, when the run
/// is loaded; buying one applies its next level to the session's config at once (<see cref="ShopRules.Buy"/>).
/// </summary>
public static class OfficeUpgrades
{
    public const string FilingCabinetId = "filing-cabinet";
    public const string SupplyClosetId = "supply-closet";
    public const string ComposingStickId = "composing-stick";
    public const string OvertimeId = "overtime";
    public const string WastebasketId = "wastebasket";
    public const string BroadsheetId = "broadsheet";
    public const string EditorialCutsId = "editorial-cuts";
    public const string PhotocopierId = "photocopier";
    public const string SavingsBondId = "savings-bond";
    public const string BonusClauseId = "bonus-clause";

    public static ImmutableArray<OfficeUpgrade> All { get; } = Catalog(OfficeConfig.Default);

    /// <summary>The catalog with <paramref name="office"/>'s level counts.</summary>
    public static ImmutableArray<OfficeUpgrade> Catalog(OfficeConfig office) =>
    [
        new(FilingCabinetId, "Filing Cabinet", office.DeskSlotLevels),
        new(SupplyClosetId, "Supply Closet", office.StationerySlotLevels),
        new(ComposingStickId, "Composing Stick", office.HandSizeLevels),
        new(OvertimeId, "Overtime", office.SubmissionLevels),
        new(WastebasketId, "Wastebasket", office.DiscardLevels),
        new(BroadsheetId, "Broadsheet", 1),
        new(EditorialCutsId, "Editorial Cuts", office.DeckMinimums.Length),
        new(PhotocopierId, "Photocopier", 1),
        new(SavingsBondId, "Savings Bond", office.InterestCaps.Length),
        new(BonusClauseId, "Bonus Clause", office.OverkillCaps.Length),
    ];

    public static OfficeUpgrade? Find(string id) => All.FirstOrDefault(u => u.Id == id);

    /// <summary>The upgrades a run can still buy (below their last level), in catalog order.</summary>
    public static IEnumerable<OfficeUpgrade> Available(RunConfig config, RunState run) =>
        Catalog(config.Office).Where(u => run.Upgrades.GetValueOrDefault(u.Id) < u.MaxLevel && CanApply(config, u.Id));

    /// <summary>An upgrade that would change nothing (a 4-slot deck's Filing Cabinet is fine; a capped one isn't) is not offered.</summary>
    private static bool CanApply(RunConfig config, string id) => id switch
    {
        FilingCabinetId => config.DeskSlots < RunState.MaxDeskSlots,
        SupplyClosetId => config.StationerySlots < RunState.MaxStationerySlots,
        BroadsheetId => config.BoardSize < config.Office.BroadsheetSize,
        _ => true,
    };

    /// <summary>Every level in <paramref name="upgrades"/> applied to <paramref name="config"/>, in catalog order.</summary>
    public static RunConfig Apply(RunConfig config, IReadOnlyDictionary<string, int> upgrades)
    {
        foreach (var upgrade in Catalog(config.Office))
            for (int level = 1; level <= upgrades.GetValueOrDefault(upgrade.Id); level++)
                config = Apply(config, upgrade.Id, level);
        return config;
    }

    /// <summary>
    /// Level <paramref name="level"/> (1-based) of upgrade <paramref name="id"/> applied to <paramref name="config"/>,
    /// which must already have the levels below it. Each level's change is independent of the other upgrades, so the
    /// order they were bought in doesn't matter.
    /// </summary>
    public static RunConfig Apply(RunConfig config, string id, int level)
    {
        var office = config.Office;
        return id switch
        {
            FilingCabinetId => config with { DeskSlots = Math.Min(RunState.MaxDeskSlots, config.DeskSlots + 1) },
            SupplyClosetId => config with { StationerySlots = Math.Min(RunState.MaxStationerySlots, config.StationerySlots + 1) },
            ComposingStickId => config with { HandSizeDelta = config.HandSizeDelta + 1 },
            OvertimeId => config with { SubmissionsDelta = config.SubmissionsDelta + 1 },
            WastebasketId => config with { DiscardsDelta = config.DiscardsDelta + 1 },
            BroadsheetId => config with { BoardSize = office.BroadsheetSize, Premiums = office.BroadsheetPremiums },
            EditorialCutsId => config with
            {
                Shop = config.Shop with { MinDeckSize = Math.Min(config.Shop.MinDeckSize, At(office.DeckMinimums, level)) },
            },
            PhotocopierId => config with { DuplicateDeskItems = true },
            SavingsBondId => config with
            {
                Economy = config.Economy with { InterestCap = Math.Max(config.Economy.InterestCap, At(office.InterestCaps, level)) },
            },
            BonusClauseId => config with
            {
                Economy = config.Economy with { OverkillCap = Math.Max(config.Economy.OverkillCap, At(office.OverkillCaps, level)) },
            },
            _ => throw new ArgumentException($"No Office upgrade '{id}'.", nameof(id)),
        };
    }

    /// <summary>What buying level <paramref name="level"/> of <paramref name="id"/> does, with the run's numbers (before it).</summary>
    public static string Describe(string id, int level, RunConfig config)
    {
        var office = config.Office;
        return id switch
        {
            FilingCabinetId => $"+1 Desk Item slot ({config.DeskSlots} → {config.DeskSlots + 1}).",
            SupplyClosetId => $"+1 Stationery slot ({config.StationerySlots} → {config.StationerySlots + 1}).",
            ComposingStickId => "+1 hand size every round.",
            OvertimeId => "+1 submission every round.",
            WastebasketId => "+1 discard every round.",
            BroadsheetId => $"The board grows to {office.BroadsheetSize}×{office.BroadsheetSize} with more premium squares "
                + "(Tight Margins still shrinks it).",
            EditorialCutsId => $"Your deck may shrink to {At(office.DeckMinimums, level)} tiles (was {config.Shop.MinDeckSize}).",
            PhotocopierId => "The shop offers Desk Items you already own: hold copies, each fires.",
            SavingsBondId => $"Interest caps at ${At(office.InterestCaps, level)} (was ${config.Economy.InterestCap}).",
            BonusClauseId => $"Overkill pays up to ${At(office.OverkillCaps, level)} (was ${config.Economy.OverkillCap}).",
            _ => throw new ArgumentException($"No Office upgrade '{id}'.", nameof(id)),
        };
    }

    private static int At(ImmutableArray<int> values, int level) => values[Math.Clamp(level, 1, values.Length) - 1];
}
