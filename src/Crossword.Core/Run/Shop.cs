using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;

namespace Crossword.Core.Run;

/// <summary>Shop prices and offer mix. PLACEHOLDER numbers pending run simulation.</summary>
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
    int RareWeight = 10)
{
    public static ShopConfig Default { get; } = new();

    public int PriceOf(IDeskItem item) => item.Rarity switch
    {
        DeskItemRarity.Uncommon => UncommonPrice,
        DeskItemRarity.Rare => RarePrice,
        _ => CommonPrice,
    };

    public int SellValueOf(IDeskItem item) => Math.Max(1, PriceOf(item) / 2);
}

public abstract record ShopOffer(int Price)
{
    public abstract string Description { get; }
}

public sealed record DeskItemOffer(IDeskItem Item, int Price) : ShopOffer(Price)
{
    public override string Description => $"{Item.Name} ({Item.Rarity}) — {Item.Description}";
}

/// <summary>Adds a new tile to the deck.</summary>
public sealed record AddTileOffer(Letter Letter, TileEnhancement Enhancement, int Price) : ShopOffer(Price)
{
    public override string Description =>
        Enhancement == TileEnhancement.None ? $"Add tile {Letter} to your deck" : $"Add {Enhancement} tile {Letter} to your deck";
}

/// <summary>Enhances one tile of the player's choice.</summary>
public sealed record EnhanceOffer(TileEnhancement Enhancement, int Price) : ShopOffer(Price)
{
    public override string Description => $"Make one of your tiles {Enhancement}";
}

/// <summary>Removes up to <see cref="MaxTiles"/> tiles of the player's choice from the deck.</summary>
public sealed record StrikeOffer(int MaxTiles, int Price) : ShopOffer(Price)
{
    public override string Description => $"Strike up to {MaxTiles} tiles from your deck";
}

/// <summary>Permanently levels up one word tier (Balatro's Planet cards).</summary>
public sealed record StyleGuideOffer(int TierMinLength, string TierLabel, long Chips, decimal Mult, int Price) : ShopOffer(Price)
{
    public override string Description => $"Style Guide: {TierLabel} words +{Chips} chips, +{Mult} mult (permanent)";
}

/// <summary>An open shop. Bought offers become null; rerolling replaces all offers.</summary>
public sealed record ShopState(ImmutableArray<ShopOffer?> Offers, int RerollCost, Random.Rng Rng);
