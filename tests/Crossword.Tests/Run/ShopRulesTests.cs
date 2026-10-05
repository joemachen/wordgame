using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Run;

public class ShopRulesTests
{
    private static readonly ShopConfig Shop = new(
        CommonPrice: 4, UncommonPrice: 6, PlainTilePrice: 2, EnhancedTilePrice: 4, EnhancePrice: 3, StrikePrice: 3,
        StrikeMaxTiles: 2, RerollBaseCost: 5, RerollStep: 1, MinDeckSize: 30);

    private static readonly RunConfig Config = RunConfig.Default with { Shop = Shop };

    private static GameSession InShop(int money, params ShopOffer?[] offers)
    {
        var run = RunState.New(1) with { Money = money };
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(Config, run, RunPhase.Shop, round,
            new ShopState(offers.ToImmutableArray(), Shop.RerollBaseCost, Rng.FromSeed(5)));
    }

    [Fact]
    public void Generate_IsDeterministic_AndExcludesOwnedItems()
    {
        var run = RunState.New(1).AddDeskItem(new RedPen()).Value;

        var (a, rngA) = ShopRules.Generate(run, Config, Rng.FromSeed(10));
        var (b, rngB) = ShopRules.Generate(run, Config, Rng.FromSeed(10));

        Assert.Equal(a.Offers, b.Offers);
        Assert.Equal(rngA, rngB);
        Assert.Equal(Shop.DeskItemOffers + Shop.EditOffers + Shop.StyleGuideOffers + Shop.StationeryOffers, a.Offers.Length);
        Assert.Single(a.Offers.OfType<StyleGuideOffer>());
        Assert.Single(a.Offers.OfType<StationeryOffer>());
        Assert.DoesNotContain(a.Offers.OfType<DeskItemOffer>(), o => o.Item.Id == "red-pen");
    }

    [Fact]
    public void BuyDeskItem_ChargesPrice_AndFillsSlot()
    {
        var session = InShop(10, new DeskItemOffer(new RedPen(), 4));

        var next = ShopRules.Buy(session, 0).Value;

        Assert.Equal(6, next.Run.Money);
        Assert.IsType<RedPen>(Assert.Single(next.Run.DeskItems));
        Assert.Null(next.Shop!.Offers[0]);
        Assert.False(ShopRules.Buy(next, 0).IsOk); // already bought
    }

    [Fact]
    public void Buy_WithoutEnoughMoney_IsRejected()
    {
        Assert.False(ShopRules.Buy(InShop(3, new DeskItemOffer(new RedPen(), 4)), 0).IsOk);
    }

    [Fact]
    public void BuyDeskItem_WhenDeskFull_IsRejected_AndCostsNothing()
    {
        var session = InShop(20, new DeskItemOffer(new RareInk(), 4));
        var full = DeskItemCatalog.All.Take(RunState.MaxDeskSlots).Aggregate(session.Run, (r, i) => r.AddDeskItem(i).Value);
        session = session with { Run = full };

        Assert.False(ShopRules.Buy(session, 0).IsOk);
    }

    [Fact]
    public void BuyAddTile_AddsTileWithFreshId()
    {
        var session = InShop(10, new AddTileOffer(Letter.From('E'), TileEnhancement.Bold, 4));

        var next = ShopRules.Buy(session, 0).Value;
        var added = next.Run.Deck[^1];

        Assert.Equal(session.Run.Deck.Length + 1, next.Run.Deck.Length);
        Assert.Equal('E', added.Letter.Char);
        Assert.Equal(TileEnhancement.Bold, added.Enhancement);
        Assert.DoesNotContain(session.Run.Deck, t => t.Id == added.Id);
    }

    [Fact]
    public void BuyEnhance_UpgradesChosenTile()
    {
        var session = InShop(10, new EnhanceOffer(TileEnhancement.Italic, 3));
        int tileId = session.Run.Deck[5].Id;

        var next = ShopRules.Buy(session, 0, [tileId]).Value;

        Assert.Equal(TileEnhancement.Italic, next.Run.Deck.Single(t => t.Id == tileId).Enhancement);
        Assert.Equal(7, next.Run.Money);
    }

    [Fact]
    public void BuyEnhance_RequiresExactlyOneDeckTile()
    {
        var session = InShop(10, new EnhanceOffer(TileEnhancement.Italic, 3));

        Assert.False(ShopRules.Buy(session, 0).IsOk);
        Assert.False(ShopRules.Buy(session, 0, [9999]).IsOk);
        Assert.False(ShopRules.Buy(session, 0, [1, 2]).IsOk);
    }

    [Fact]
    public void BuyStrike_RemovesChosenTiles_WithinLimits()
    {
        var session = InShop(10, new StrikeOffer(2, 3));
        int[] ids = [session.Run.Deck[0].Id, session.Run.Deck[1].Id];

        var next = ShopRules.Buy(session, 0, ids).Value;

        Assert.Equal(session.Run.Deck.Length - 2, next.Run.Deck.Length);
        Assert.False(ShopRules.Buy(session, 0, session.Run.Deck.Take(3).Select(t => t.Id).ToArray()).IsOk);
    }

    [Fact]
    public void BuyStrike_CannotShrinkDeckBelowMinimum()
    {
        var session = InShop(10, new StrikeOffer(2, 3));
        session = session with { Run = session.Run with { Deck = session.Run.Deck.Take(31).ToImmutableArray() } };

        Assert.False(ShopRules.Buy(session, 0, [session.Run.Deck[0].Id, session.Run.Deck[1].Id]).IsOk);
    }

    [Fact]
    public void Generate_NamesStyleGuides_ByTier()
    {
        var (shop, _) = ShopRules.Generate(RunState.New(1), Config, Rng.FromSeed(10));

        var guide = Assert.Single(shop.Offers.OfType<StyleGuideOffer>());
        Assert.Equal(StyleGuideNames.For(guide.TierMinLength, guide.TierLabel), guide.Name);
        Assert.StartsWith(guide.Name, guide.Description);
    }

    [Fact]
    public void StyleGuideNames_CoverDefaultTiers_AndFallBackForOthers()
    {
        Assert.Equal("Chicago Manual of Style", StyleGuideNames.For(4, "4-letter"));
        Assert.Equal("The Lexicographer's Omnibus", StyleGuideNames.For(7, "7+-letter"));
        Assert.Equal("9+-letter Style Guide", StyleGuideNames.For(9, "9+-letter"));
        Assert.All(ScoringConfig.Default.Tiers, t => Assert.DoesNotContain("Style Guide", StyleGuideNames.For(t.MinLength, "x")));
    }

    [Fact]
    public void BuyStyleGuide_UpgradesTier_AndStacks()
    {
        var session = InShop(10, new StyleGuideOffer(4, "Chicago", "4-letter", 10, 1, 3), new StyleGuideOffer(4, "Chicago", "4-letter", 10, 1, 3));

        var next = ShopRules.Buy(ShopRules.Buy(session, 0).Value, 1).Value;

        Assert.Equal(2, next.Run.TierUpgrades[4]);
        Assert.Equal(4, next.Run.Money);
        var tier = next.Scoring.TierFor(4);
        var baseTier = ScoringConfig.Default.TierFor(4);
        Assert.Equal(baseTier.BaseChips + 2 * baseTier.LevelChips, tier.BaseChips);
        Assert.Equal(baseTier.BaseMult + 2 * baseTier.LevelMult, tier.BaseMult);
    }

    [Fact]
    public void Reroll_ChargesIncreasingCost_AndReplacesOffers()
    {
        var session = InShop(20, null, null);

        var once = ShopRules.Reroll(session).Value;
        var twice = ShopRules.Reroll(once).Value;

        Assert.Equal(15, once.Run.Money);
        Assert.Equal(9, twice.Run.Money);
        Assert.Equal(7, twice.Shop!.RerollCost);
        Assert.All(once.Shop!.Offers, Assert.NotNull);
    }

    [Fact]
    public void Reroll_WithoutEnoughMoney_IsRejected()
    {
        Assert.False(ShopRules.Reroll(InShop(4)).IsOk);
    }

    [Fact]
    public void Sell_ReturnsHalfPrice()
    {
        var session = InShop(0);
        session = session with { Run = session.Run.AddDeskItem(new CrossReference()).Value };

        var next = ShopRules.Sell(session, 0).Value;

        Assert.Empty(next.Run.DeskItems);
        Assert.Equal(3, next.Run.Money); // uncommon $6 → $3
    }

    [Fact]
    public void Buy_OutsideShop_IsRejected()
    {
        var session = InShop(10, new DeskItemOffer(new RedPen(), 4)) with { Phase = RunPhase.InRound };

        Assert.False(ShopRules.Buy(session, 0).IsOk);
    }
}
