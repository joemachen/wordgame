using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Run;

public class StationeryTests
{
    private static readonly ShopConfig Shop = ShopConfig.Default with
    {
        StationeryPrice = 3,
        StationeryOffers = 1,
        StationeryIds = new HashSet<string> { "answer-key" },
    };
    private static readonly RunConfig Config = RunConfig.Default with { Shop = Shop };

    private static GameSession InShop(int money, params ShopOffer?[] offers)
    {
        var run = RunState.New(1) with { Money = money };
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 0, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(Config, run, RunPhase.Shop, round,
            new ShopState(offers.ToImmutableArray(), Shop.RerollBaseCost, Rng.FromSeed(5)));
    }

    private static GameSession InRoundWith(params IStationery[] stationery)
    {
        var session = RunRules.NewGame(7, Config, LexiconLoader.Enable);
        return session with { Run = session.Run with { Stationery = stationery.ToImmutableArray() } };
    }

    /// <summary>A round on a 5×5 board with CAT across the top row, hand CATSORE (ids 0..6) and a bag of Es.</summary>
    private static GameSession KnownRound(int discards = 3, params IStationery[] stationery)
    {
        var bag = new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('E'))).ToImmutableArray());
        var round = new RoundState(new RoundConfig(TargetScore: 1000, BoardSize: 5, Discards: discards),
            BoardFromRows("CAT..", ".....", ".....", ".....", "....."), bag, HandOf("CATSORE"), Rng.FromSeed(5),
            Score: 0, SubmissionsLeft: 4, DiscardsLeft: discards);
        return new GameSession(Config, RunState.New(1) with { Stationery = stationery.ToImmutableArray() }, RunPhase.InRound, round);
    }

    /// <summary>
    /// One discard left, a hand and bag of Qs (no word in the fixture lexicon): discarding the hand leaves no legal
    /// play and no discards.
    /// </summary>
    private static GameSession AboutToDeadlock(int bagQs = 10, params IStationery[] stationery)
    {
        var session = KnownRound(discards: 1, stationery);
        var qs = new TileBag(Enumerable.Range(100, bagQs).Select(i => new Tile(i, Letter.From('Q'))).ToImmutableArray());
        return session with { Round = session.Round with { Hand = HandOf("QQQQQQQ"), Bag = qs } };
    }

    private static GameSession DiscardHand(GameSession session) =>
        RunRules.Discard(session, session.Round.Hand.Tiles.Select(t => t.Id).ToArray(), Words).Value;

    // ---------------------------------------------------------------- shop

    [Fact]
    public void Generate_OffersStationeryAtItsPrice()
    {
        var (shop, _) = ShopRules.Generate(RunState.New(1), Config, Rng.FromSeed(10));

        var offer = Assert.Single(shop.Offers.OfType<StationeryOffer>());
        Assert.IsType<AnswerKey>(offer.Item);
        Assert.Equal(3, offer.Price);
    }

    [Fact]
    [Trait("Category", "Determinism")]
    public void Generate_DrawsFromTheWholeCatalog_Deterministically()
    {
        var config = RunConfig.Default with { Shop = ShopConfig.Default with { StationeryOffers = 1 } };
        string OfferFor(ulong seed) =>
            ShopRules.Generate(RunState.New(1), config, Rng.FromSeed(seed)).Shop.Offers.OfType<StationeryOffer>().Single().Item.Id;

        var offered = Enumerable.Range(1, 60).Select(i => OfferFor((ulong)i)).ToList();

        Assert.True(offered.Distinct().Count() > 2);
        Assert.All(offered, id => Assert.NotNull(StationeryCatalog.Find(id)));
        Assert.Equal(offered, Enumerable.Range(1, 60).Select(i => OfferFor((ulong)i)));
    }

    [Fact]
    public void PerItemPrice_OverridesTheFlatPrice_ForOffersAndSelling()
    {
        var shop = Shop with
        {
            StationeryIds = new HashSet<string> { "margin-clip" },
            StationeryPrices = new Dictionary<string, int> { ["margin-clip"] = 6 },
        };
        var (generated, _) = ShopRules.Generate(RunState.New(1), Config with { Shop = shop }, Rng.FromSeed(10));

        Assert.Equal(6, Assert.Single(generated.Offers.OfType<StationeryOffer>()).Price);
        Assert.Equal(3, shop.SellValueOf(new MarginClip()));
        Assert.Equal(3, shop.PriceOf(new AnswerKey()));
        Assert.Equal(1, shop.SellValueOf(new AnswerKey()));
    }

    [Fact]
    public void Buy_ChargesPrice_AndFillsASlot()
    {
        var next = ShopRules.Buy(InShop(10, new StationeryOffer(new AnswerKey(), 3)), 0).Value;

        Assert.Equal(7, next.Run.Money);
        Assert.IsType<AnswerKey>(Assert.Single(next.Run.Stationery));
    }

    [Fact]
    public void Buy_WhenSlotsFull_IsRejected()
    {
        var session = InShop(10, new StationeryOffer(new AnswerKey(), 3));
        session = session with
        {
            Run = session.Run with { Stationery = Enumerable.Repeat<IStationery>(new AnswerKey(), RunState.MaxStationerySlots).ToImmutableArray() },
        };

        Assert.False(ShopRules.Buy(session, 0).IsOk);
    }

    [Fact]
    public void SellStationery_RefundsHalfThePrice()
    {
        var session = InShop(0) with { Run = RunState.New(1) with { Stationery = [new AnswerKey()] } };

        var sold = ShopRules.SellStationery(session, 0, Words).Value;

        Assert.Empty(sold.Run.Stationery);
        Assert.Equal(1, sold.Run.Money);
        Assert.False(ShopRules.SellStationery(sold, 0, Words).IsOk);
    }

    [Fact]
    public void AddStationery_AllowsDuplicates_UpToTheSlotLimit()
    {
        var run = RunState.New(1);
        for (int i = 0; i < RunState.MaxStationerySlots; i++)
            run = run.AddStationery(new AnswerKey()).Value;

        Assert.False(run.AddStationery(new AnswerKey()).IsOk);
    }

    // ---------------------------------------------------------------- using

    [Fact]
    public void UseStationery_OutsideARound_IsRejected()
    {
        var session = InShop(0) with { Run = RunState.New(1) with { Stationery = [new AnswerKey()] } };

        Assert.False(RunRules.UseStationery(session, 0, LexiconLoader.Enable).IsOk);
    }

    [Fact]
    public void UseStationery_EmptySlot_IsRejected()
    {
        Assert.False(RunRules.UseStationery(InRoundWith(), 0, LexiconLoader.Enable).IsOk);
    }

    [Fact]
    public void AnswerKey_RevealsTheBestPlay_AndIsConsumed()
    {
        var session = InRoundWith(new AnswerKey(), new AnswerKey());
        var round = session.Round;
        var ranked = MoveRanker.Rank(round.Board, round.Hand, LexiconLoader.Enable, session.Run.DeskItems,
            round.Config.EffectiveScoring(session.Scoring), round.Config.MinWordLength, RoundRules.Environment(round, session.Run.Money));

        var used = RunRules.UseStationery(session, 0, LexiconLoader.Enable).Value;

        Assert.Single(used.Session.Run.Stationery);
        Assert.Equal(ranked[0].Score.Total, used.Play!.Score.Total);
        Assert.Equal(session.Round, used.Session.Round); // revealing the play changes nothing else
        Assert.True(RunRules.Submit(used.Session, used.Play.Play.Placed, LexiconLoader.Enable).IsOk);
    }

    // ---------------------------------------------------------------- the rule-breakers (2026-10-10)

    [Fact]
    public void GoldStar_PutsAPremiumOnAnEmptySquare_ThatPaysWhenATileLandsThere()
    {
        var session = KnownRound(stationery: new GoldStar(Premium.DoubleWord));
        var placed = Spell(session.Round.Board, session.Round.Hand, 1, 1, Direction.Across, "TO");
        var plain = RunRules.Submit(session, placed, Words).Value.Score;

        var starred = RunRules.UseStationery(session, 0, Words, cell: new Position(1, 2)).Value.Session;
        var scored = RunRules.Submit(starred, placed, Words).Value.Score;

        Assert.Equal(Premium.DoubleWord, starred.Round.Board.PremiumAt(new Position(1, 2)));
        Assert.Empty(starred.Run.Stationery);
        Assert.Equal(plain.WordChips[0] * 2, scored.WordChips[0]); // TO, the main word, runs over the star
    }

    [Fact]
    public void GoldStar_NeedsAnEmptyUnblockedSquare_OrItIsKept()
    {
        var session = KnownRound(stationery: new GoldStar());
        var blocked = session with { Round = session.Round with { Board = session.Round.Board with { Blocked = [new Position(4, 4)] } } };

        Assert.False(RunRules.UseStationery(session, 0, Words).IsOk);
        Assert.False(RunRules.UseStationery(session, 0, Words, cell: new Position(0, 0)).IsOk); // occupied
        Assert.False(RunRules.UseStationery(blocked, 0, Words, cell: new Position(4, 4)).IsOk);
        Assert.False(RunRules.UseStationery(session, 0, Words, cell: new Position(9, 9)).IsOk);
        Assert.Single(session.Run.Stationery);
    }

    [Fact]
    public void Clipping_ScoresTheChosenBoardWordAgainOnTheNextPlay_ThenEnds()
    {
        var session = KnownRound(stationery: new Clipping());
        var placed = Spell(session.Round.Board, session.Round.Hand, 1, 1, Direction.Across, "TO");
        var plain = RunRules.Submit(session, placed, Words).Value.Score;

        var clipped = RunRules.UseStationery(session, 0, Words, cell: new Position(0, 1)).Value.Session;
        var scored = RunRules.Submit(clipped, placed, Words).Value;

        Assert.Equal(new ClippedWord(new Position(0, 0), Direction.Across, "CAT"), clipped.Round.Config.Clipping);
        Assert.Equal(plain.Chips + 5, scored.Score.Chips); // C3 + A1 + T1, no premiums
        Assert.Contains(scored.Score.Log, e => e.SourceId == ScoringEngine.Sources.Clipping && e.Description.Contains("CAT again, +5 chips"));
        Assert.Null(scored.Session.Round.Config.Clipping);
    }

    [Fact]
    public void Clipping_PicksTheLongerWordThroughTheCell_AndNeedsAWord()
    {
        // CAT across the top with TO under AT: the A at (0,1) is in CAT (3) and AT (2).
        var session = KnownRound(stationery: new Clipping());
        session = RunRules.Submit(session, Spell(session.Round.Board, session.Round.Hand, 1, 1, Direction.Across, "TO"), Words).Value.Session;

        var clipped = RunRules.UseStationery(session, 0, Words, cell: new Position(0, 1)).Value.Session;

        Assert.Equal("CAT", clipped.Round.Config.Clipping!.Text);
        Assert.False(RunRules.UseStationery(session, 0, Words, cell: new Position(4, 4)).IsOk); // nothing there
        Assert.False(RunRules.UseStationery(session, 0, Words).IsOk);
    }

    [Fact]
    public void Clipping_PrintsNothingWhenTheWordWasCut_AndIgnoresRedundantCopy()
    {
        var session = KnownRound(stationery: [new Clipping(), new WhiteOut()]);
        var clipped = RunRules.UseStationery(session, 0, Words, cell: new Position(0, 1)).Value.Session;
        var cut = RunRules.UseStationery(clipped, 0, Words, cell: new Position(0, 0)).Value.Session; // the C is gone
        var afterCut = RunRules.Submit(cut, Spell(cut.Round.Board, cut.Round.Hand, 1, 1, Direction.Across, "TO"), Words).Value.Score;
        Assert.DoesNotContain(afterCut.Log, e => e.SourceId == ScoringEngine.Sources.Clipping);

        var redundant = clipped with
        {
            Round = clipped.Round with
            {
                Config = new RedundantCopy().Apply(clipped.Round.Config),
                WordsFormed = ["CAT"],
            },
        };
        var reprinted = RunRules.Submit(redundant, Spell(redundant.Round.Board, redundant.Round.Hand, 1, 1, Direction.Across, "TO"), Words).Value.Score;
        Assert.Contains(reprinted.Log, e => e.SourceId == ScoringEngine.Sources.Clipping && e.Description.Contains("+5 chips"));
    }

    [Fact]
    public void PoeticLicense_LetsOneNonWordThrough_OncePerLicence()
    {
        var session = KnownRound(stationery: new PoeticLicense());
        var cr = Spell(session.Round.Board, session.Round.Hand, 1, 0, Direction.Across, "R"); // CR down: not a word
        Assert.False(RunRules.Submit(session, cr, Words).IsOk);

        var licensed = RunRules.UseStationery(session, 0, Words).Value.Session;
        Assert.Equal(1, licensed.Round.Config.IllegalWordsAllowed);
        var scored = RunRules.Submit(licensed, cr, Words).Value;

        Assert.Contains(scored.Score.Play.Words, w => w.Text == "CR");
        Assert.True(scored.Score.Total > 0);
        Assert.Equal(0, scored.Session.Round.Config.IllegalWordsAllowed);
        Assert.False(RunRules.Submit(scored.Session, Spell(scored.Session.Round.Board, scored.Session.Round.Hand, 2, 0, Direction.Across, "S"), Words).IsOk);
    }

    [Fact]
    public void PoeticLicense_CoversOneDistinctNonWord_NotTwo()
    {
        var session = KnownRound(stationery: new PoeticLicense());
        var licensed = RunRules.UseStationery(session, 0, Words).Value.Session;
        var rs = Spell(licensed.Round.Board, licensed.Round.Hand, 1, 0, Direction.Across, "RS"); // RS and CR are both non-words

        Assert.False(RunRules.Submit(licensed, rs, Words).IsOk);
    }

    [Fact]
    public void PoeticLicense_KeepsAStuckHandAlive_AndCountsAsALegalPlay()
    {
        var session = DiscardHand(AboutToDeadlock(stationery: new PoeticLicense()));
        Assert.Equal(RoundStatus.InProgress, session.Round.Status); // the licence could still be used

        var licensed = RunRules.UseStationery(session, 0, Words).Value.Session;

        Assert.Equal(RoundStatus.InProgress, licensed.Round.Status);
        Assert.True(RoundRules.HasLegalPlay(licensed.Round, Words)); // a Q next to CAT forms a licensed non-word
        Assert.True(StationeryCatalog.CanEscape([new PoeticLicense()], session.Round));
    }

    [Fact]
    public void Highlighter_TriplesOneTilesLetterValueOnTheNextPlay_ThenEnds()
    {
        var session = KnownRound(stationery: new Highlighter(Factor: 3));
        var cats = Spell(session.Round.Board, session.Round.Hand, 0, 0, Direction.Across, "CATS"); // places the S (id 3)
        var plain = RunRules.Submit(session, cats, Words).Value.Score;

        var marked = RunRules.UseStationery(session, 0, Words, tileIds: [3]).Value.Session;
        var scored = RunRules.Submit(marked, cats, Words).Value;

        Assert.Equal(new TileHighlight(3, 3), marked.Round.Config.Highlight);
        Assert.Equal(plain.WordChips[0] + 2, scored.Score.WordChips[0]); // S is worth 3 instead of 1
        Assert.Contains(scored.Score.Log, e => e.Description.Contains("highlighted S ×3"));
        Assert.Null(scored.Session.Round.Config.Highlight);
    }

    [Fact]
    public void Highlighter_EndsWhenTheTileLeavesTheHand_AndRefusesWilds()
    {
        var session = KnownRound(stationery: new Highlighter());
        var marked = RunRules.UseStationery(session, 0, Words, tileIds: [3]).Value.Session;
        var discarded = RunRules.Discard(marked, [3], Words).Value;
        Assert.Null(discarded.Round.Config.Highlight);

        var wild = session with { Round = session.Round with { Hand = new Hand(session.Round.Hand.Tiles.Replace(session.Round.Hand.Tiles[3], Tile.Wild(3))) } };
        Assert.False(RunRules.UseStationery(wild, 0, Words, tileIds: [3]).IsOk);
        Assert.False(RunRules.UseStationery(session, 0, Words, tileIds: [3, 4]).IsOk);
        Assert.Single(session.Run.Stationery);
    }

    [Fact]
    public void CorrectionTape_TakesBackThePlayJustMade_RoundDeskItemsAndMoney()
    {
        var session = KnownRound(stationery: new CorrectionTape());
        session = session with { Run = session.Run with { Money = 5, DeskItems = [new WordCount(ChipsPerTile: 2, Chips: 0)] } };
        var played = RunRules.Submit(session, Spell(session.Round.Board, session.Round.Hand, 1, 1, Direction.Across, "TO"), Words).Value.Session;
        Assert.Equal(3, played.Round.SubmissionsLeft);
        Assert.Equal(4L, ((WordCount)played.Run.DeskItems[0]).Chips);
        Assert.NotNull(played.Round.Undo);

        var undone = RunRules.UseStationery(played, 0, Words).Value.Session;

        Assert.Equal(0, undone.Round.Score);
        Assert.Equal(4, undone.Round.SubmissionsLeft);
        Assert.Equal(session.Round.Hand.Tiles.Select(t => t.Id).Order(), undone.Round.Hand.Tiles.Select(t => t.Id).Order());
        Assert.Equal(session.Round.Board.Cells.ToArray(), undone.Round.Board.Cells.ToArray());
        Assert.Equal(session.Round.Bag.Tiles.ToArray(), undone.Round.Bag.Tiles.ToArray());
        Assert.Equal(0L, ((WordCount)undone.Run.DeskItems[0]).Chips);
        Assert.Equal(5, undone.Run.Money);
        Assert.Empty(undone.Run.Stationery);
        Assert.Null(undone.Round.Undo);
    }

    [Fact]
    public void CorrectionTape_OnlyWorksRightAfterAPlay()
    {
        var fresh = KnownRound(stationery: new CorrectionTape());
        Assert.False(RunRules.UseStationery(fresh, 0, Words).IsOk);
        Assert.False(StationeryCatalog.CanEscape([new CorrectionTape()], fresh.Round));

        var played = RunRules.Submit(fresh, Spell(fresh.Round.Board, fresh.Round.Hand, 1, 1, Direction.Across, "TO"), Words).Value.Session;
        Assert.True(StationeryCatalog.CanEscape([new CorrectionTape()], played.Round));
        var discarded = RunRules.Discard(played, [played.Round.Hand.Tiles[0].Id], Words).Value;
        Assert.Null(discarded.Round.Undo);
        Assert.False(RunRules.UseStationery(discarded, 0, Words).IsOk);
        Assert.Single(discarded.Run.Stationery);
    }

    [Fact]
    public void Catalog_ListsTheNewItems_WithTheirTargets()
    {
        Assert.Equal(11, StationeryCatalog.All.Length);
        Assert.Equal(StationeryTarget.EmptyCell, StationeryCatalog.Find("gold-star")!.Target);
        Assert.Equal(StationeryTarget.BoardWord, StationeryCatalog.Find("clipping")!.Target);
        Assert.Equal(StationeryTarget.HandTiles, StationeryCatalog.Find("highlighter")!.Target);
        Assert.Equal(StationeryTarget.None, StationeryCatalog.Find("poetic-license")!.Target);
        Assert.Equal(StationeryTarget.None, StationeryCatalog.Find("correction-tape")!.Target);
    }

    [Fact]
    public void MarginClip_AddsSubmissions_AndIsConsumed()
    {
        var session = KnownRound(stationery: new MarginClip(Submissions: 2));

        var used = RunRules.UseStationery(session, 0, Words).Value;

        Assert.Equal(6, used.Session.Round.SubmissionsLeft);
        Assert.Empty(used.Session.Run.Stationery);
        Assert.Null(used.Play);
    }

    [Fact]
    public void RedInkBottle_AddsMultToEveryPlay_AndStacks()
    {
        var session = KnownRound(stationery: [new RedInkBottle(Mult: 3), new RedInkBottle(Mult: 3)]);
        var placed = Spell(session.Round.Board, session.Round.Hand, 1, 1, Direction.Across, "TO");
        var plain = RunRules.Submit(session, placed, Words).Value.Score;

        var once = RunRules.UseStationery(session, 0, Words).Value.Session;
        var twice = RunRules.UseStationery(once, 0, Words).Value.Session;
        var scored = RunRules.Submit(twice, placed, Words).Value;

        Assert.Equal(6m, twice.Round.Config.BonusMult);
        Assert.Equal(plain.Mult + 6, scored.Score.Mult);
        Assert.Contains(scored.Score.Log, e => e.SourceId == ScoringEngine.Sources.Bonus);
        Assert.Equal(6m, scored.Session.Round.Config.BonusMult); // lasts the whole round
    }

    [Fact]
    public void Scissors_RedrawsChosenTiles_WithoutSpendingADiscard()
    {
        var session = KnownRound(stationery: new Scissors(MaxTiles: 2));

        var used = RunRules.UseStationery(session, 0, Words, tileIds: [0, 1]).Value.Session;

        Assert.Equal(3, used.Round.DiscardsLeft);
        Assert.Equal(7, used.Round.Hand.Count);
        Assert.False(used.Round.Hand.Contains(0) || used.Round.Hand.Contains(1));
        Assert.Equal(18, used.Round.Bag.Tiles.Length);
        Assert.Empty(used.Run.Stationery);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0, 1, 2 })]
    [InlineData(new[] { 42 })]
    public void Scissors_WithBadSelection_FailsAndIsKept(int[] tileIds)
    {
        var session = KnownRound(stationery: new Scissors(MaxTiles: 2));

        Assert.False(RunRules.UseStationery(session, 0, Words, tileIds: tileIds).IsOk);
        Assert.Single(session.Run.Stationery);
    }

    [Fact]
    public void Scissors_WithAnEmptyBag_Fails()
    {
        var session = KnownRound(stationery: new Scissors());
        session = session with { Round = session.Round with { Bag = TileBag.Empty } };

        Assert.False(RunRules.UseStationery(session, 0, Words, tileIds: [0]).IsOk);
    }

    [Fact]
    public void WhiteOut_RemovesABoardTile_AndTheCellIsPlayableAgain()
    {
        var session = KnownRound(stationery: new WhiteOut());
        var cell = new Position(0, 1);

        var used = RunRules.UseStationery(session, 0, Words, cell: cell).Value.Session;

        Assert.Null(used.Round.Board.TileAt(cell));
        Assert.Empty(used.Run.Stationery);
        var a = used.Round.Hand.Tiles.First(t => t.Letter.Char == 'A');
        Assert.True(RunRules.Submit(used, [new PlacedTile(cell, a)], Words).IsOk); // C·T → CAT again
    }

    [Fact]
    public void WhiteOut_WithoutATileThere_FailsAndIsKept()
    {
        var session = KnownRound(stationery: new WhiteOut());

        Assert.False(RunRules.UseStationery(session, 0, Words, cell: new Position(3, 3)).IsOk);
        Assert.False(RunRules.UseStationery(session, 0, Words).IsOk);
        Assert.Single(session.Run.Stationery);
    }

    // ---------------------------------------------------------------- deadlocks

    [Fact]
    public void Deadlock_WithoutStationery_LosesTheRound()
    {
        Assert.Equal(RunPhase.Defeat, DiscardHand(AboutToDeadlock()).Phase);
    }

    [Fact]
    public void Deadlock_WithAnEscapeItemHeld_KeepsTheRoundGoing()
    {
        Assert.Equal(RunPhase.InRound, DiscardHand(AboutToDeadlock(stationery: new Scissors())).Phase);
        Assert.Equal(RunPhase.InRound, DiscardHand(AboutToDeadlock(stationery: new WhiteOut())).Phase);
        Assert.Equal(RunPhase.Defeat, DiscardHand(AboutToDeadlock(stationery: new MarginClip())).Phase);
    }

    [Fact]
    public void Deadlock_ScissorsWithAnEmptyBag_CannotEscape()
    {
        // Exactly 7 Qs in the bag: discarding the hand draws all of them.
        Assert.Equal(RunPhase.Defeat, DiscardHand(AboutToDeadlock(bagQs: 7, stationery: new Scissors())).Phase);
    }

    [Fact]
    public void Deadlock_SellingTheEscapeItem_LosesTheRound()
    {
        var stuck = DiscardHand(AboutToDeadlock(stationery: new WhiteOut()));

        Assert.Equal(RunPhase.Defeat, ShopRules.SellStationery(stuck, 0, Words).Value.Phase);
    }

    [Fact]
    public void Deadlock_UsingTheEscapeItemWithoutEscaping_LosesTheRound()
    {
        var stuck = DiscardHand(AboutToDeadlock(stationery: new Scissors()));

        var used = RunRules.UseStationery(stuck, 0, Words, tileIds: [stuck.Round.Hand.Tiles[0].Id]).Value.Session;

        Assert.Equal(RunPhase.Defeat, used.Phase); // redrew another Q
    }
}
