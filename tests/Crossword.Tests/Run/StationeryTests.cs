using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Stationery;

namespace Crossword.Tests.Run;

public class StationeryTests
{
    private static readonly ShopConfig Shop = ShopConfig.Default with { StationeryPrice = 3, StationeryOffers = 1 };
    private static readonly RunConfig Config = RunConfig.Default with { Shop = Shop };

    private static GameSession InShop(int money, params ShopOffer?[] offers)
    {
        var run = RunState.New(1) with { Money = money };
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(Config, run, RunPhase.Shop, round,
            new ShopState(offers.ToImmutableArray(), Shop.RerollBaseCost, Rng.FromSeed(5)));
    }

    private static GameSession InRoundWith(params IStationery[] stationery)
    {
        var session = RunRules.NewGame(7, Config, LexiconLoader.Enable);
        return session with { Run = session.Run with { Stationery = stationery.ToImmutableArray() } };
    }

    [Fact]
    public void Generate_OffersStationeryAtItsPrice()
    {
        var (shop, _) = ShopRules.Generate(RunState.New(1), Config, Rng.FromSeed(10));

        var offer = Assert.Single(shop.Offers.OfType<StationeryOffer>());
        Assert.IsType<AnswerKey>(offer.Item);
        Assert.Equal(3, offer.Price);
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

        var sold = ShopRules.SellStationery(session, 0).Value;

        Assert.Empty(sold.Run.Stationery);
        Assert.Equal(1, sold.Run.Money);
        Assert.False(ShopRules.SellStationery(sold, 0).IsOk);
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
    public void AddStationery_AllowsDuplicates_UpToTheSlotLimit()
    {
        var run = RunState.New(1);
        for (int i = 0; i < RunState.MaxStationerySlots; i++)
            run = run.AddStationery(new AnswerKey()).Value;

        Assert.False(run.AddStationery(new AnswerKey()).IsOk);
    }
}
