using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Stationery;

namespace Crossword.Core.Run;

/// <summary>Pure shop transitions. Each shop gets its own RNG stream split off the run RNG.</summary>
public static class ShopRules
{
    private static readonly TileEnhancement[] Enhancements = [TileEnhancement.Bold, TileEnhancement.Italic, TileEnhancement.Gilded];

    /// <summary>Opens a shop; returns it with the advanced run RNG.</summary>
    public static (ShopState Shop, Rng RunRng) Generate(RunState run, RunConfig config, Rng runRng)
    {
        var (seed, nextRunRng) = runRng.NextUInt64();
        var (offers, rng) = RollOffers(run, config, Rng.FromSeed(seed));
        return (new ShopState(offers, config.Shop.RerollBaseCost, rng), nextRunRng);
    }

    /// <summary>
    /// Buys offer <paramref name="index"/>. Enhance needs exactly one tile id from the deck; Strike needs
    /// 1..MaxTiles ids. Other offers ignore <paramref name="tileIds"/>.
    /// </summary>
    public static Result<GameSession, string> Buy(GameSession session, int index, IReadOnlyCollection<int>? tileIds = null)
    {
        if (session.Phase != RunPhase.Shop || session.Shop is not { } shop)
            return Fail("The shop isn't open.");
        if (index < 0 || index >= shop.Offers.Length || shop.Offers[index] is not { } offer)
            return Fail("No offer there.");
        if (session.Run.Money < offer.Price)
            return Fail($"Costs ${offer.Price}; you have ${session.Run.Money}.");

        var applied = Apply(session.Run, offer, tileIds ?? [], session.Config.Shop, session.Config.DeskSlots);
        if (!applied.IsOk)
            return Fail(applied.Error);

        return Result<GameSession, string>.Ok(session with
        {
            Run = applied.Value with { Money = applied.Value.Money - offer.Price },
            Shop = shop with { Offers = shop.Offers.SetItem(index, null) },
        });
    }

    /// <summary>Sells the Desk Item in <paramref name="slot"/> for half its price. Allowed during rounds and in the shop.</summary>
    public static Result<GameSession, string> Sell(GameSession session, int slot)
    {
        if (session.Phase is not (RunPhase.InRound or RunPhase.Shop))
            return Fail("Nothing to sell now.");
        if (slot < 0 || slot >= session.Run.DeskItems.Length)
            return Fail($"No desk item in slot {slot + 1}.");

        int value = session.Config.Shop.SellValueOf(session.Run.DeskItems[slot]);
        var removed = session.Run.RemoveDeskItem(slot).Value;
        return Result<GameSession, string>.Ok(session with { Run = removed with { Money = removed.Money + value } });
    }

    /// <summary>
    /// Sells the Stationery in <paramref name="slot"/> for half its price. Allowed during rounds and in the shop; in a
    /// round, selling the last item that could break a deadlock (Scissors, White-Out) can end the round.
    /// </summary>
    public static Result<GameSession, string> SellStationery(GameSession session, int slot, IWordGraph lexicon)
    {
        if (session.Phase is not (RunPhase.InRound or RunPhase.Shop))
            return Fail("Nothing to sell now.");
        if (slot < 0 || slot >= session.Run.Stationery.Length)
            return Fail($"No stationery in slot {slot + 1}.");

        int value = session.Config.Shop.SellValueOf(session.Run.Stationery[slot]);
        var removed = session.Run.RemoveStationery(slot).Value;
        return Result<GameSession, string>.Ok(RunRules.Recheck(session with { Run = removed with { Money = removed.Money + value } }, lexicon));
    }

    public static Result<GameSession, string> Reroll(GameSession session)
    {
        if (session.Phase != RunPhase.Shop || session.Shop is not { } shop)
            return Fail("The shop isn't open.");
        if (session.Run.Money < shop.RerollCost)
            return Fail($"Reroll costs ${shop.RerollCost}; you have ${session.Run.Money}.");

        var (offers, rng) = RollOffers(session.Run, session.Config, shop.Rng);
        return Result<GameSession, string>.Ok(session with
        {
            Run = session.Run with { Money = session.Run.Money - shop.RerollCost },
            Shop = new ShopState(offers, shop.RerollCost + session.Config.Shop.RerollStep, rng),
        });
    }

    private static Result<RunState, string> Apply(RunState run, ShopOffer offer, IReadOnlyCollection<int> tileIds, ShopConfig config,
        int deskSlots)
    {
        switch (offer)
        {
            case DeskItemOffer desk:
                return run.AddDeskItem(desk.Item, deskSlots);

            case AddTileOffer add:
                int id = run.Deck.IsEmpty ? 0 : run.Deck.Max(t => t.Id) + 1;
                var added = add.Wild ? Tile.Wild(id, add.Enhancement) : new Tile(id, add.Letter, add.Enhancement);
                return Result<RunState, string>.Ok(run with { Deck = run.Deck.Add(added) });

            case WildOffer:
                if (tileIds.Count != 1)
                    return Result<RunState, string>.Fail("Choose exactly one tile to make wild.");
                int wildIndex = run.Deck.ToList().FindIndex(t => t.Id == tileIds.First());
                if (wildIndex < 0)
                    return Result<RunState, string>.Fail("That tile isn't in your deck.");
                if (run.Deck[wildIndex].IsWild)
                    return Result<RunState, string>.Fail("That tile is already wild.");
                return Result<RunState, string>.Ok(run with
                {
                    Deck = run.Deck.SetItem(wildIndex, Tile.Wild(run.Deck[wildIndex].Id, run.Deck[wildIndex].Enhancement)),
                });

            case EnhanceOffer enhance:
                if (tileIds.Count != 1)
                    return Result<RunState, string>.Fail("Choose exactly one tile to enhance.");
                int index = run.Deck.ToList().FindIndex(t => t.Id == tileIds.First());
                if (index < 0)
                    return Result<RunState, string>.Fail("That tile isn't in your deck.");
                if (run.Deck[index].Enhancement == enhance.Enhancement)
                    return Result<RunState, string>.Fail($"That tile is already {enhance.Enhancement}.");
                return Result<RunState, string>.Ok(run with
                {
                    Deck = run.Deck.SetItem(index, run.Deck[index] with { Enhancement = enhance.Enhancement }),
                });

            case StationeryOffer stationery:
                return run.AddStationery(stationery.Item);

            case StyleGuideOffer guide:
                return Result<RunState, string>.Ok(run.UpgradeTier(guide.TierMinLength));

            case StrikeOffer strike:
                var ids = tileIds.ToHashSet();
                if (ids.Count == 0 || ids.Count > strike.MaxTiles)
                    return Result<RunState, string>.Fail($"Choose 1 to {strike.MaxTiles} tiles to strike.");
                if (!ids.All(i => run.Deck.Any(t => t.Id == i)))
                    return Result<RunState, string>.Fail("Those tiles aren't all in your deck.");
                if (run.Deck.Length - ids.Count < config.MinDeckSize)
                    return Result<RunState, string>.Fail($"Your deck can't go below {config.MinDeckSize} tiles.");
                return Result<RunState, string>.Ok(run with { Deck = run.Deck.RemoveAll(t => ids.Contains(t.Id)) });

            default:
                return Result<RunState, string>.Fail($"Unknown offer {offer.GetType().Name}.");
        }
    }

    private static (ImmutableArray<ShopOffer?> Offers, Rng Rng) RollOffers(RunState run, RunConfig runConfig, Rng rng)
    {
        var config = runConfig.Shop;
        var offers = ImmutableArray.CreateBuilder<ShopOffer?>();

        var unowned = DeskItemCatalog.All
            .Where(item => config.DeskItemIds?.Contains(item.Id) ?? true)
            .Where(item => run.DeskItems.All(owned => owned.Id != item.Id))
            .ToList();
        for (int i = 0; i < config.DeskItemOffers && unowned.Count > 0; i++)
        {
            (var rarity, rng) = RollRarity(config, rng);
            var pool = unowned.Where(item => item.Rarity == rarity).ToList();
            if (pool.Count == 0)
                pool = unowned;
            (int pick, rng) = rng.NextInt(pool.Count);
            offers.Add(new DeskItemOffer(pool[pick], config.PriceOf(pool[pick])));
            unowned.Remove(pool[pick]);
        }

        var letterPool = StartingDeck.Create().Where(t => !t.IsWild).ToImmutableArray();
        for (int i = 0; i < config.EditOffers; i++)
        {
            (int roll, rng) = rng.NextInt(100);
            if (roll < 50)
            {
                (int wildRoll, rng) = rng.NextInt(100);
                if (wildRoll < config.WildTilePercent)
                {
                    offers.Add(new AddTileOffer(Tile.WildPlaceholder, TileEnhancement.None, config.WildTilePrice, Wild: true));
                    continue;
                }
                (int pick, rng) = rng.NextInt(letterPool.Length);
                (int enhancedRoll, rng) = rng.NextInt(100);
                var enhancement = TileEnhancement.None;
                if (enhancedRoll < config.EnhancedTilePercent)
                    (enhancement, rng) = PickEnhancement(rng);
                offers.Add(new AddTileOffer(letterPool[pick].Letter, enhancement,
                    enhancement == TileEnhancement.None ? config.PlainTilePrice : config.EnhancedTilePrice));
            }
            else if (roll < 50 + config.WildEditPercent)
            {
                offers.Add(new WildOffer(config.WildEditPrice));
            }
            else if (roll < 80)
            {
                (var enhancement, rng) = PickEnhancement(rng);
                offers.Add(new EnhanceOffer(enhancement, config.EnhancePrice));
            }
            else
            {
                offers.Add(new StrikeOffer(config.StrikeMaxTiles, config.StrikePrice));
            }
        }

        var tiers = runConfig.Scoring.Tiers;
        for (int i = 0; i < config.StyleGuideOffers; i++)
        {
            (int t, rng) = rng.NextInt(tiers.Length);
            var tier = tiers[t];
            string label = tier.Label(t == tiers.Length - 1);
            offers.Add(new StyleGuideOffer(tier.MinLength, StyleGuideNames.For(tier.MinLength, label), label, tier.LevelChips,
                tier.LevelMult, config.StyleGuidePrice));
        }

        // Rolled last so the Stationery pool never shifts the other offers. A single-entry pool consumes no RNG.
        var stationery = config.StationeryIds is { } ids
            ? StationeryCatalog.All.Where(s => ids.Contains(s.Id)).ToImmutableArray()
            : StationeryCatalog.All;
        for (int i = 0; i < config.StationeryOffers && stationery.Length > 0; i++)
        {
            int pick = 0;
            if (stationery.Length > 1)
                (pick, rng) = rng.NextInt(stationery.Length);
            offers.Add(new StationeryOffer(stationery[pick], config.PriceOf(stationery[pick])));
        }

        return (offers.ToImmutable(), rng);
    }

    private static (DeskItemRarity, Rng) RollRarity(ShopConfig config, Rng rng)
    {
        var weights = config.RarityWeights().ToList();
        var (roll, next) = rng.NextInt(weights.Sum(w => w.Weight));
        int running = 0;
        foreach (var (rarity, weight) in weights)
        {
            running += weight;
            if (roll < running)
                return (rarity, next);
        }
        return (weights[^1].Rarity, next);
    }

    private static (TileEnhancement, Rng) PickEnhancement(Rng rng)
    {
        var (i, next) = rng.NextInt(Enhancements.Length);
        return (Enhancements[i], next);
    }

    private static Result<GameSession, string> Fail(string message) => Result<GameSession, string>.Fail(message);
}
