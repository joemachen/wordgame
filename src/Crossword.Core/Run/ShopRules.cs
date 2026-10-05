using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Random;

namespace Crossword.Core.Run;

/// <summary>Pure shop transitions. Each shop gets its own RNG stream split off the run RNG.</summary>
public static class ShopRules
{
    private static readonly TileEnhancement[] Enhancements = [TileEnhancement.Bold, TileEnhancement.Italic, TileEnhancement.Gilded];

    /// <summary>Opens a shop; returns it with the advanced run RNG.</summary>
    public static (ShopState Shop, Rng RunRng) Generate(RunState run, ShopConfig config, Rng runRng)
    {
        var (seed, nextRunRng) = runRng.NextUInt64();
        var (offers, rng) = RollOffers(run, config, Rng.FromSeed(seed));
        return (new ShopState(offers, config.RerollBaseCost, rng), nextRunRng);
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

        var applied = Apply(session.Run, offer, tileIds ?? [], session.Config.Shop);
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

    public static Result<GameSession, string> Reroll(GameSession session)
    {
        if (session.Phase != RunPhase.Shop || session.Shop is not { } shop)
            return Fail("The shop isn't open.");
        if (session.Run.Money < shop.RerollCost)
            return Fail($"Reroll costs ${shop.RerollCost}; you have ${session.Run.Money}.");

        var (offers, rng) = RollOffers(session.Run, session.Config.Shop, shop.Rng);
        return Result<GameSession, string>.Ok(session with
        {
            Run = session.Run with { Money = session.Run.Money - shop.RerollCost },
            Shop = new ShopState(offers, shop.RerollCost + session.Config.Shop.RerollStep, rng),
        });
    }

    private static Result<RunState, string> Apply(RunState run, ShopOffer offer, IReadOnlyCollection<int> tileIds, ShopConfig config)
    {
        switch (offer)
        {
            case DeskItemOffer desk:
                return run.AddDeskItem(desk.Item);

            case AddTileOffer add:
                int id = run.Deck.IsEmpty ? 0 : run.Deck.Max(t => t.Id) + 1;
                return Result<RunState, string>.Ok(run with { Deck = run.Deck.Add(new Tile(id, add.Letter, add.Enhancement)) });

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

    private static (ImmutableArray<ShopOffer?> Offers, Rng Rng) RollOffers(RunState run, ShopConfig config, Rng rng)
    {
        var offers = ImmutableArray.CreateBuilder<ShopOffer?>();

        var unowned = DeskItemCatalog.All.Where(item => run.DeskItems.All(owned => owned.Id != item.Id)).ToList();
        (var shuffled, rng) = rng.Shuffle(unowned);
        foreach (var item in shuffled.Take(config.DeskItemOffers))
            offers.Add(new DeskItemOffer(item, config.PriceOf(item)));

        var letterPool = StartingDeck.Create();
        for (int i = 0; i < config.EditOffers; i++)
        {
            (int roll, rng) = rng.NextInt(100);
            if (roll < 50)
            {
                (int pick, rng) = rng.NextInt(letterPool.Length);
                (int enhancedRoll, rng) = rng.NextInt(100);
                var enhancement = TileEnhancement.None;
                if (enhancedRoll < config.EnhancedTilePercent)
                    (enhancement, rng) = PickEnhancement(rng);
                offers.Add(new AddTileOffer(letterPool[pick].Letter, enhancement,
                    enhancement == TileEnhancement.None ? config.PlainTilePrice : config.EnhancedTilePrice));
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

        return (offers.ToImmutable(), rng);
    }

    private static (TileEnhancement, Rng) PickEnhancement(Rng rng)
    {
        var (i, next) = rng.NextInt(Enhancements.Length);
        return (Enhancements[i], next);
    }

    private static Result<GameSession, string> Fail(string message) => Result<GameSession, string>.Fail(message);
}
