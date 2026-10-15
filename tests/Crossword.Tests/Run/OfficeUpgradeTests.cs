using System.Collections.Immutable;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using Crossword.Tests.TestSupport;

namespace Crossword.Tests.Run;

public class OfficeUpgradeTests
{
    private static readonly OfficeConfig Office = new(DeskSlotLevels: 2, StationerySlotLevels: 2, HandSizeLevels: 2, SubmissionLevels: 1,
        DiscardLevels: 2, DoubleTruckSize: 9)
    {
        DoubleTruckPremiums = new PremiumPairs(TripleWord: 2, DoubleWord: 3, TripleLetter: 2, DoubleLetter: 3),
        DeckMinimums = [20, 15],
        InterestCaps = [10, 20],
        OverkillCaps = [6, 12],
    };

    private static readonly RunConfig Config = RunConfig.Default with
    {
        Office = Office,
        Economy = new EconomyConfig(OverkillStep: 0.25m, OverkillCap: 3, InterestPer: 4, InterestCap: 5) { MinPaycheck = 0 },
        Shop = ShopConfig.Default with { UpgradeOffers = 1, UpgradePrice = 10, UpgradePrices = null, MinDeckSize = 30 },
    };

    private static RunConfig With(params (string Id, int Level)[] upgrades) =>
        OfficeUpgrades.Apply(Config, upgrades.ToImmutableDictionary(u => u.Id, u => u.Level));

    private static GameSession InShop(RunConfig config, RunState run, params ShopOffer?[] offers)
    {
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(config, run, RunPhase.Shop, round, new ShopState(offers.ToImmutableArray(), 5, Rng.FromSeed(5)));
    }

    private static UpgradeOffer Offer(string id, int level, RunConfig? config = null) =>
        new(id, level, OfficeUpgrades.Find(id)!.Name, OfficeUpgrades.Describe(id, level, config ?? Config), 10);

    [Fact]
    public void EachUpgrade_ChangesItsRule()
    {
        Assert.Equal(6, With((OfficeUpgrades.FilingCabinetId, 1)).DeskSlots);
        Assert.Equal(7, With((OfficeUpgrades.FilingCabinetId, 2)).DeskSlots);
        Assert.Equal(4, With((OfficeUpgrades.SupplyClosetId, 2)).StationerySlots);
        Assert.Equal(2, With((OfficeUpgrades.ComposingStickId, 2)).HandSizeDelta);
        Assert.Equal(1, With((OfficeUpgrades.OvertimeId, 1)).SubmissionsDelta);
        Assert.Equal(2, With((OfficeUpgrades.WastebasketId, 2)).DiscardsDelta);
        var doubleTruck = With((OfficeUpgrades.DoubleTruckId, 1));
        Assert.Equal(9, doubleTruck.BoardSize);
        Assert.Equal(Office.DoubleTruckPremiums, doubleTruck.Premiums);
        Assert.Equal(20, With((OfficeUpgrades.EditorialCutsId, 1)).Shop.MinDeckSize);
        Assert.Equal(15, With((OfficeUpgrades.EditorialCutsId, 2)).Shop.MinDeckSize);
        Assert.True(With((OfficeUpgrades.PhotocopierId, 1)).DuplicateDeskItems);
        Assert.Equal(20, With((OfficeUpgrades.TrustFundId, 2)).Economy.InterestCap);
        Assert.Equal(6, With((OfficeUpgrades.BonusClauseId, 1)).Economy.OverkillCap);
        Assert.Same(Config, OfficeUpgrades.Apply(Config, ImmutableDictionary<string, int>.Empty));
    }

    [Fact]
    public void RoundRules_TakeTheUpgradedNumbers_AfterTheBoss()
    {
        var config = With((OfficeUpgrades.ComposingStickId, 1), (OfficeUpgrades.OvertimeId, 1), (OfficeUpgrades.WastebasketId, 1),
            (OfficeUpgrades.DoubleTruckId, 1));

        var daily = config.RoundConfigFor(0);
        Assert.Equal((9, 5, 4, 8), (daily.BoardSize, daily.Submissions, daily.Discards, daily.HandSize));
        Assert.Equal(Office.DoubleTruckPremiums, daily.PremiumPairs);

        // Tight Margins still shrinks the board; Tight Deadline's 3 submissions get the Overtime one on top.
        var margins = config.RoundConfigFor(2, new TightMargins(5));
        Assert.Equal(5, margins.BoardSize);
        Assert.Equal(4, config.RoundConfigFor(2, new TightDeadline(3)).Submissions);
        // Rush Job's cap of 3 applies before Overtime's +1.
        Assert.Equal(4, (config with { BossSubmissionsCap = 3 }).RoundConfigFor(2, new InkSpill()).Submissions);
    }

    [Fact]
    public void DoubleTruck_StartsA9x9Round()
    {
        var session = RunRules.NewGame(4, Config, LexiconLoader.Enable,
            run => run with { Upgrades = run.Upgrades.SetItem(OfficeUpgrades.DoubleTruckId, 1) });

        Assert.Equal(9, session.Round.Board.Size);
        Assert.Equal(9, session.Config.BoardSize);
        int premiums = Enumerable.Range(0, 81).Count(i => session.Round.Board.PremiumAt(new Position(i / 9, i % 9)) != Premium.None);
        Assert.Equal(Office.DoubleTruckPremiums.Total * 2, premiums);
    }

    [Fact]
    public void Buying_StoresTheLevel_AndChangesTheRulesAtOnce()
    {
        var session = InShop(Config, RunState.New(1) with { Money = 25 }, Offer(OfficeUpgrades.FilingCabinetId, 1));

        var bought = ShopRules.Buy(session, 0).Value;

        Assert.Equal(15, bought.Run.Money);
        Assert.Equal(1, bought.Run.Upgrades[OfficeUpgrades.FilingCabinetId]);
        Assert.Equal(6, bought.Config.DeskSlots);
        Assert.Null(bought.Shop!.Offers[0]);
        Assert.Equal(RunRules.ConfigFor(Config, bought.Run).DeskSlots, bought.Config.DeskSlots);
    }

    [Fact]
    public void Buying_AStaleLevel_Fails()
    {
        var run = RunState.New(1) with { Money = 25, Upgrades = ImmutableDictionary<string, int>.Empty.Add(OfficeUpgrades.OvertimeId, 1) };

        Assert.False(ShopRules.Buy(InShop(With((OfficeUpgrades.OvertimeId, 1)), run, Offer(OfficeUpgrades.OvertimeId, 1)), 0).IsOk);
    }

    [Fact]
    public void Shop_OffersOneUpgrade_Last_AndNeverOneAtItsLastLevel()
    {
        var run = RunState.New(1);
        var (with, _) = ShopRules.Generate(run, Config, Rng.FromSeed(10));
        var (without, _) = ShopRules.Generate(run, Config with { Shop = Config.Shop with { UpgradeOffers = 0 } }, Rng.FromSeed(10));

        Assert.IsType<UpgradeOffer>(with.Offers[^1]);
        Assert.Equal(without.Offers, with.Offers.RemoveAt(with.Offers.Length - 1)); // rolled last: nothing else moves

        var maxed = run with
        {
            Upgrades = OfficeUpgrades.Catalog(Office).Where(u => u.Id != OfficeUpgrades.WastebasketId).ToImmutableDictionary(u => u.Id, u => u.MaxLevel)
                .Add(OfficeUpgrades.WastebasketId, 1),
        };
        var config = OfficeUpgrades.Apply(Config, maxed.Upgrades);
        for (ulong seed = 0; seed < 20; seed++)
        {
            var offer = Assert.IsType<UpgradeOffer>(ShopRules.Generate(maxed, config, Rng.FromSeed(seed)).Shop.Offers[^1]);
            Assert.Equal((OfficeUpgrades.WastebasketId, 2), (offer.UpgradeId, offer.Level));
        }
        var all = maxed with { Upgrades = maxed.Upgrades.SetItem(OfficeUpgrades.WastebasketId, 2) };
        Assert.Empty(ShopRules.Generate(all, OfficeUpgrades.Apply(Config, all.Upgrades), Rng.FromSeed(3)).Shop.Offers.OfType<UpgradeOffer>());
    }

    [Fact]
    public void FilingCabinet_IsNotOffered_AtTheSlotCap()
    {
        var capped = Config with { DeskSlots = RunState.MaxDeskSlots, Shop = Config.Shop with { UpgradeIds = new HashSet<string> { OfficeUpgrades.FilingCabinetId } } };

        Assert.Empty(ShopRules.Generate(RunState.New(1), capped, Rng.FromSeed(1)).Shop.Offers.OfType<UpgradeOffer>());
    }

    [Fact]
    public void FilingCabinet_BuildsOnAFourSlotDeck()
    {
        var copyEditor = RunRules.ConfigFor(Config, Decks.CopyEditorId, 1);
        Assert.Equal(4, copyEditor.DeskSlots);

        var run = RunState.New(1) with { DeckId = Decks.CopyEditorId, Upgrades = ImmutableDictionary<string, int>.Empty.Add(OfficeUpgrades.FilingCabinetId, 2) };

        Assert.Equal(6, RunRules.ConfigFor(Config, run).DeskSlots);
    }

    [Fact]
    public void SupplyCloset_HoldsMoreStationery()
    {
        var config = With((OfficeUpgrades.SupplyClosetId, 1));
        var run = RunState.New(1) with { Money = 30, Stationery = [new AnswerKey(), new AnswerKey()] };

        Assert.False(ShopRules.Buy(InShop(Config, run, new StationeryOffer(new Scissors(), 3)), 0).IsOk);
        Assert.Equal(3, ShopRules.Buy(InShop(config, run, new StationeryOffer(new Scissors(), 3)), 0).Value.Run.Stationery.Length);
    }

    [Fact]
    public void Photocopier_OffersOwnedItems_AndCopiesBothFire()
    {
        var owned = DeskItemCatalog.All.Take(DeskItemCatalog.All.Length - 1).ToImmutableArray();
        var run = RunState.New(1) with { DeskItems = owned };
        var only = Config.Shop with { DeskItemIds = new HashSet<string> { owned[0].Id }, UpgradeOffers = 0 };

        Assert.Empty(ShopRules.Generate(run, Config with { Shop = only }, Rng.FromSeed(2)).Shop.Offers.OfType<DeskItemOffer>());
        var copier = With((OfficeUpgrades.PhotocopierId, 1)) with { Shop = only };
        Assert.Contains(ShopRules.Generate(run, copier, Rng.FromSeed(2)).Shop.Offers.OfType<DeskItemOffer>(), o => o.Item.Id == owned[0].Id);

        var pen = RunState.New(1) with { Money = 10, DeskItems = [new RedPen(2)] };
        Assert.False(ShopRules.Buy(InShop(Config, pen, new DeskItemOffer(new RedPen(2), 4)), 0).IsOk);
        var twoPens = ShopRules.Buy(InShop(With((OfficeUpgrades.PhotocopierId, 1)), pen, new DeskItemOffer(new RedPen(2), 4)), 0).Value.Run.DeskItems;
        Assert.Equal(2, twoPens.Length);

        var play = Fixtures.PlayOn(Board.Empty(7), "CAT", 3, 2, Direction.Across, "CAT");
        var scoring = ScoringConfig.Default;
        decimal baseMult = ScoringEngine.Score(play, [], scoring).Mult;
        Assert.Equal(baseMult + 4, ScoringEngine.Score(play, twoPens, scoring).Mult);
    }

    [Fact]
    public void EditorialCuts_LetsTheDeckShrinkBelowThirty()
    {
        var run = RunState.New(1) with { Money = 10 };
        run = run with { Deck = run.Deck.Take(31).ToImmutableArray() };
        var strike = new StrikeOffer(2, 3);
        int[] ids = [run.Deck[0].Id, run.Deck[1].Id];

        Assert.False(ShopRules.Buy(InShop(Config, run, strike), 0, ids).IsOk);
        Assert.Equal(29, ShopRules.Buy(InShop(With((OfficeUpgrades.EditorialCutsId, 1)), run, strike), 0, ids).Value.Run.Deck.Length);
    }

    [Fact]
    public void TrustFund_AndBonusClause_RaiseTheirCaps()
    {
        var kind = new RoundKind("Daily", 1m, BasePay: 3, IsBoss: false);
        var round = new RoundState(new RoundConfig(TargetScore: 100), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1000, SubmissionsLeft: 0, DiscardsLeft: 0);

        var plain = Economy.Calculate(Config.Economy, kind, round, moneyBeforePayout: 100);
        Assert.Equal((3, 5), (plain.Overkill, plain.Interest));

        var upgraded = With((OfficeUpgrades.TrustFundId, 2), (OfficeUpgrades.BonusClauseId, 2));
        var payout = Economy.Calculate(upgraded.Economy, kind, round, moneyBeforePayout: 100);
        Assert.Equal((12, 20), (payout.Overkill, payout.Interest));
    }

    [Fact]
    public void Upgrades_SurviveSaveAndLoad_AndAreReapplied()
    {
        var session = RunRules.NewGame(9, Config, LexiconLoader.Enable, run => run with
        {
            Upgrades = ImmutableDictionary<string, int>.Empty.Add(OfficeUpgrades.FilingCabinetId, 2).Add(OfficeUpgrades.OvertimeId, 1),
        });
        Assert.Equal(7, session.Config.DeskSlots);
        Assert.Equal(5, session.Round.Config.Submissions);

        var loaded = RunSaveJson.Deserialize(RunSaveJson.Serialize(session), Config).Value.Session;

        Assert.Equal(session.Run.Upgrades, loaded.Run.Upgrades);
        Assert.Equal(7, loaded.Config.DeskSlots);
        Assert.Equal(1, loaded.Config.SubmissionsDelta);
    }

    [Fact]
    public void SaveWithAnUnknownUpgrade_FailsWithoutThrowing()
    {
        var session = RunRules.NewGame(9, Config, LexiconLoader.Enable, run => run with
        {
            Upgrades = ImmutableDictionary<string, int>.Empty.Add(OfficeUpgrades.OvertimeId, 1),
        });
        string json = RunSaveJson.Serialize(session).Replace("\"overtime\"", "\"time-machine\"");

        Assert.False(RunSaveJson.Deserialize(json, Config).IsOk);
    }

    [Fact]
    public void Catalog_IdsAreUnique_AndEveryLevelDescribes()
    {
        Assert.Equal(OfficeUpgrades.All.Length, OfficeUpgrades.All.Select(u => u.Id).Distinct().Count());
        // 'give <id>' and --give share one namespace with Desk Items and Stationery, and names show side by side.
        var others = DeskItemCatalog.All.Select(i => (i.Id, i.Name)).Concat(StationeryCatalog.All.Select(i => (i.Id, i.Name))).ToList();
        Assert.DoesNotContain(OfficeUpgrades.All, u => others.Any(o => o.Id == u.Id || o.Name == u.Name));
        foreach (var upgrade in OfficeUpgrades.All)
            for (int level = 1; level <= upgrade.MaxLevel; level++)
                Assert.False(string.IsNullOrWhiteSpace(OfficeUpgrades.Describe(upgrade.Id, level, Config)));
    }
}
