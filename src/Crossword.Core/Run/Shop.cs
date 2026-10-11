using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;

namespace Crossword.Core.Run;

/// <summary>
/// Shop prices and offer mix. PLACEHOLDER numbers pending run simulation.
/// <see cref="DeskItemIds"/> / <see cref="StationeryIds"/> limit the Desk Item / Stationery pools to those ids
/// (null = the whole catalog; used by balance harnesses);
/// <see cref="StationeryPrices"/> overrides <see cref="StationeryPrice"/> per item id.
/// Wild tiles: <see cref="WildTilePercent"/> of add-tile offers add a wild tile; <see cref="WildEditPercent"/> of all
/// edit offers make a chosen deck tile wild.
/// </summary>
public sealed record ShopConfig(
    int DeskItemOffers = 2,
    int EditOffers = 2,
    int CommonPrice = 4,
    int UncommonPrice = 6,
    int RarePrice = 8,
    int PlainTilePrice = 2,
    int EnhancedTilePrice = 4,
    int EnhancePrice = 3,
    int StrikePrice = 3,
    int StrikeMaxTiles = 2,
    int RerollBaseCost = 5,
    int RerollStep = 1,
    int MinDeckSize = 30,
    int EnhancedTilePercent = 35,
    int StyleGuideOffers = 1,
    int StyleGuidePrice = 3,
    int CommonWeight = 60,
    int UncommonWeight = 30,
    int RareWeight = 10,
    int StationeryOffers = 1,
    int StationeryPrice = 3,
    IReadOnlySet<string>? StationeryIds = null,
    IReadOnlyDictionary<string, int>? StationeryPrices = null,
    int WildTilePercent = 12,
    int WildTilePrice = 6,
    int WildEditPercent = 10,
    int WildEditPrice = 5,
    IReadOnlySet<string>? DeskItemIds = null)
{
    /// <summary>
    /// Margin Clip costs $6: at the flat $3 it alone added +13 pts of win rate (ScoreFraction 0.75). Price is a weak lever
    /// for it — $5–$7 measure alike — because an extra submission saves runs.
    /// </summary>
    public static ShopConfig Default { get; } = new(StationeryPrices: new Dictionary<string, int> { ["margin-clip"] = 6 });

    public int PriceOf(IDeskItem item) => item.Rarity switch
    {
        DeskItemRarity.Uncommon => UncommonPrice,
        DeskItemRarity.Rare => RarePrice,
        _ => CommonPrice,
    };

    public int SellValueOf(IDeskItem item) => Math.Max(1, PriceOf(item) / 2);

    public int PriceOf(IStationery item) =>
        StationeryPrices is not null && StationeryPrices.TryGetValue(item.Id, out int price) ? price : StationeryPrice;

    public int SellValueOf(IStationery item) => Math.Max(1, PriceOf(item) / 2);
}

public abstract record ShopOffer(int Price)
{
    public abstract string Description { get; }

    /// <summary>
    /// <see cref="Description"/> plus what a tile enhancement does, with the run's numbers
    /// (<see cref="Scoring.ScoringConfig.Describe"/>); the same as <see cref="Description"/> for other offers.
    /// </summary>
    public virtual string Describe(ScoringConfig scoring) => Description;
}

public sealed record DeskItemOffer(IDeskItem Item, int Price) : ShopOffer(Price)
{
    public override string Description => $"{Item.Name} ({Item.Rarity}) — {Item.Description}";
}

/// <summary>Adds a new tile to the deck (a wild tile when <see cref="Wild"/>; its letter is then ignored).</summary>
public sealed record AddTileOffer(Letter Letter, TileEnhancement Enhancement, int Price, bool Wild = false) : ShopOffer(Price)
{
    public override string Description =>
        Wild ? "Add a wild tile (plays as any letter, 0 chips) to your deck"
        : Enhancement == TileEnhancement.None ? $"Add tile {Letter} to your deck" : $"Add {Enhancement} tile {Letter} to your deck";

    public override string Describe(ScoringConfig scoring) =>
        Wild || Enhancement == TileEnhancement.None ? Description : $"{Description} — {scoring.Describe(Enhancement)}";
}

/// <summary>Makes one tile of the player's choice wild (it keeps its enhancement).</summary>
public sealed record WildOffer(int Price) : ShopOffer(Price)
{
    public override string Description => "Make one of your tiles wild (plays as any letter, 0 chips)";
}

/// <summary>Enhances one tile of the player's choice.</summary>
public sealed record EnhanceOffer(TileEnhancement Enhancement, int Price) : ShopOffer(Price)
{
    public override string Description => $"Make one of your tiles {Enhancement}";

    public override string Describe(ScoringConfig scoring) => $"{Description} — {scoring.Describe(Enhancement)}";
}

/// <summary>Removes up to <see cref="MaxTiles"/> tiles of the player's choice from the deck.</summary>
public sealed record StrikeOffer(int MaxTiles, int Price) : ShopOffer(Price)
{
    public override string Description => $"Strike up to {MaxTiles} tiles from your deck";
}

/// <summary>Permanently levels up one word tier (Balatro's Planet cards).</summary>
public sealed record StyleGuideOffer(int TierMinLength, string Name, string TierLabel, long Chips, decimal Mult, int Price) : ShopOffer(Price)
{
    public override string Description => $"{Name}: {TierLabel} words +{Chips} chips, +{Mult} mult (permanent)";
}

/// <summary>A one-shot Stationery item, kept in a Stationery slot until used.</summary>
public sealed record StationeryOffer(IStationery Item, int Price) : ShopOffer(Price)
{
    public override string Description => $"{Item.Name} (Stationery) — {Item.Description}";
}

/// <summary>Player-facing Style Guide names, one per word tier (keyed by the tier's minimum length).</summary>
public static class StyleGuideNames
{
    private static readonly ImmutableDictionary<int, string> Names = new Dictionary<int, string>
    {
        [2] = "Pulp Paperbacks",
        [3] = "The Pocket Dictionary",
        [4] = "Chicago Manual of Style",
        [5] = "Unabridged Dictionary",
        [6] = "Gridiron Gazette",
        [7] = "The Lexicographer's Omnibus",
    }.ToImmutableDictionary();

    /// <summary>The guide for a tier; tiers without a name get "{tierLabel} Style Guide".</summary>
    public static string For(int tierMinLength, string tierLabel) =>
        Names.TryGetValue(tierMinLength, out var name) ? name : $"{tierLabel} Style Guide";
}

/// <summary>An open shop. Bought offers become null; rerolling replaces all offers.</summary>
public sealed record ShopState(ImmutableArray<ShopOffer?> Offers, int RerollCost, Random.Rng Rng);
