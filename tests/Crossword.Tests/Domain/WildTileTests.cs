using System.Collections.Immutable;
using Crossword.Core.Analysis;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Crossword.Core.Stationery;
using Crossword.Cli;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Domain;

public class WildTileTests
{
    private static readonly ScoringConfig Scoring = ScoringConfig.Default with
    {
        Tiers = [new WordTier(2, 2, 1), new WordTier(3, 5, 1), new WordTier(4, 10, 2)],
        IntersectionMult = 2,
    };

    /// <summary>Hand CT plus a wild (id 9).</summary>
    private static Hand HandWithWild(string letters = "CT") => new([.. HandOf(letters).Tiles, Tile.Wild(9)]);

    private static List<PlacedTile> CatWithWildA(Hand hand) =>
    [
        new(new Position(0, 0), hand.Tiles[0]),
        new(new Position(0, 1), hand.Tiles[2].As(Letter.From('A'))),
        new(new Position(0, 2), hand.Tiles[1]),
    ];

    // ---------------------------------------------------------------- tiles, validation, scoring

    [Fact]
    public void Wild_ShowsAsQuestionMark_AndPlaysAsTheChosenLetter()
    {
        var wild = Tile.Wild(9, TileEnhancement.Bold);
        var played = wild.As(Letter.From('Q'));

        Assert.Equal("?", wild.ToString());
        Assert.Equal(('Q', true, TileEnhancement.Bold, 9), (played.Letter.Char, played.IsWild, played.Enhancement, played.Id));
        Assert.Throws<InvalidOperationException>(() => new Tile(1, Letter.From('A')).As(Letter.From('B')));
    }

    [Fact]
    [Trait("Category", "Scoring")]
    public void Wild_ScoresZeroLetterChips_ButCountsForTheWord()
    {
        var hand = HandWithWild();
        var play = PlacementValidator.Validate(Board.Empty(5), hand, CatWithWildA(hand), Words).Value;

        var score = ScoringEngine.Score(play, [], Scoring);

        Assert.Equal("CAT", play.Words[0].Text);
        Assert.Equal(5 + 3 + 0 + 1, score.Chips); // tier 5, C 3, wild A 0, T 1
        Assert.Equal(0, Scoring.ValueOf(hand.Tiles[2]));
    }

    [Fact]
    public void OnlyWildTiles_MayChangeTheirLetter()
    {
        var hand = HandOf("CTA");
        var forged = new List<PlacedTile>
        {
            new(new Position(0, 0), hand.Tiles[0]),
            new(new Position(0, 1), hand.Tiles[2] with { Letter = Letter.From('O') }),
        };

        var result = PlacementValidator.Validate(Board.Empty(5), hand, forged, Words);

        Assert.IsType<PlacementError.TileChanged>(result.Error);
    }

    [Fact]
    public void AWildMustStayWild_WhenPlaced()
    {
        var hand = HandWithWild();
        var placed = CatWithWildA(hand);
        placed[1] = placed[1] with { Tile = placed[1].Tile with { IsWild = false } };

        Assert.IsType<PlacementError.TileChanged>(PlacementValidator.Validate(Board.Empty(5), hand, placed, Words).Error);
    }

    // ---------------------------------------------------------------- move generation

    [Fact]
    public void MoveGenerator_UsesAWild_ForAMissingLetter()
    {
        var plays = MoveGenerator.LegalPlays(Board.Empty(5), HandWithWild(), Words).ToList();

        var cat = plays.FirstOrDefault(p => p.Words[0].Text == "CAT" && p.Placed.Length == 3);
        Assert.NotNull(cat);
        Assert.Contains(cat.Placed, p => p.Tile.IsWild && p.Tile.Letter.Char == 'A');
    }

    [Fact]
    public void MoveGenerator_KeepsOnePlayPerPlacement_WhateverTheWildStandsFor()
    {
        var plays = MoveGenerator.LegalPlays(Board.Empty(5), HandWithWild("T"), Words).ToList();

        var placements = plays.Select(p => string.Join(';', p.Placed.Select(t => $"{t.Position}#{t.Tile.Id}").Order())).ToList();
        Assert.Equal(placements.Count, placements.Distinct().Count());
        Assert.NotEmpty(plays); // e.g. AT / TA / TO with the wild
    }

    [Fact]
    public void MoveGenerator_PrefersARealTile_OverTheWild()
    {
        var plays = MoveGenerator.LegalPlays(Board.Empty(5), HandWithWild("CAT"), Words).ToList();

        Assert.Contains(plays, p => p.Words[0].Text == "CAT" && p.Placed.All(t => !t.Tile.IsWild));
    }

    // ---------------------------------------------------------------- deck, draws, sort

    [Fact]
    public void Sort_PutsWildsLast()
    {
        var hand = new Hand([Tile.Wild(9), .. HandOf("TAC").Tiles]);

        Assert.Equal([1, 2, 0, 9], HandArrangement.Sort(hand));
        Assert.Equal([0, 2, 1, 9], HandArrangement.Sort(hand, descending: true));
    }

    [Fact]
    public void BalancedDraws_CountWildsAsNeitherVowelNorConsonant()
    {
        var bag = new TileBag([Tile.Wild(100), Tile.Wild(101), new Tile(102, Letter.From('E')), new Tile(103, Letter.From('A'))]);

        var (drawn, _, _) = bag.DrawBalanced(2, HandOf("BCDFG").Tiles, DrawConfig.Balanced, Rng.FromSeed(1));

        Assert.All(drawn, t => Assert.True(DrawConfig.IsVowel(t))); // both slots forced to real vowels
    }

    // ---------------------------------------------------------------- shop

    private static GameSession InShop(int money, params ShopOffer?[] offers)
    {
        var run = RunState.New(1) with { Money = money };
        var round = new RoundState(new RoundConfig(TargetScore: 1), Board.Empty(7), TileBag.Empty, Hand.Empty,
            Rng.FromSeed(1), Score: 1, SubmissionsLeft: 0, DiscardsLeft: 0);
        return new GameSession(RunConfig.Default, run, RunPhase.Shop, round,
            new ShopState(offers.ToImmutableArray(), 5, Rng.FromSeed(5)));
    }

    [Fact]
    public void Shop_WildTileOffer_AddsAWildTile()
    {
        var session = InShop(10, new AddTileOffer(Tile.WildPlaceholder, TileEnhancement.None, 6, Wild: true));

        var bought = ShopRules.Buy(session, 0).Value;

        Assert.Equal(session.Run.Deck.Count(t => t.IsWild) + 1, bought.Run.Deck.Count(t => t.IsWild));
        Assert.Equal(4, bought.Run.Money);
    }

    [Fact]
    public void Shop_WildEdit_MakesTheChosenTileWild_KeepingItsEnhancement()
    {
        var session = InShop(10, new WildOffer(5));
        var target = session.Run.Deck.First(t => !t.IsWild) with { Enhancement = TileEnhancement.Italic };
        session = session with { Run = session.Run with { Deck = session.Run.Deck.Replace(session.Run.Deck.First(t => t.Id == target.Id), target) } };

        var bought = ShopRules.Buy(session, 0, [target.Id]).Value;
        var tile = bought.Run.Deck.First(t => t.Id == target.Id);

        Assert.True(tile.IsWild);
        Assert.Equal(TileEnhancement.Italic, tile.Enhancement);
        Assert.False(ShopRules.Buy(session, 0, []).IsOk);
        var alreadyWild = session.Run.Deck.First(t => t.IsWild).Id;
        Assert.False(ShopRules.Buy(session, 0, [alreadyWild]).IsOk);
    }

    [Fact]
    public void Shop_RollsWildOffers_AtTheirConfiguredRates()
    {
        var shop = ShopConfig.Default with { EditOffers = 40, WildTilePercent = 100, WildEditPercent = 30 };
        var (generated, _) = ShopRules.Generate(RunState.New(1), RunConfig.Default with { Shop = shop }, Rng.FromSeed(4));

        var adds = generated.Offers.OfType<AddTileOffer>().ToList();
        Assert.NotEmpty(adds);
        Assert.All(adds, a => Assert.True(a.Wild && a.Price == shop.WildTilePrice));
        Assert.Contains(generated.Offers, o => o is WildOffer w && w.Price == shop.WildEditPrice);
    }

    // ---------------------------------------------------------------- Fountain Pen

    private static GameSession InRound(params IStationery[] held)
    {
        var bag = new TileBag(Enumerable.Range(100, 20).Select(i => new Tile(i, Letter.From('E'))).ToImmutableArray());
        var round = new RoundState(new RoundConfig(TargetScore: 1000, BoardSize: 5), Board.Empty(5), bag, HandOf("CATSORE"),
            Rng.FromSeed(5), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3);
        return new GameSession(RunConfig.Default, RunState.New(1) with { Stationery = [.. held] }, RunPhase.InRound, round);
    }

    [Fact]
    public void FountainPen_TurnsTheSelectedHandTileWild()
    {
        var used = RunRules.UseStationery(InRound(new FountainPen()), 0, Words, tileIds: [2]).Value.Session;

        Assert.True(used.Round.Hand.Tiles.First(t => t.Id == 2).IsWild);
        Assert.Equal(1, used.Round.Hand.Tiles.Count(t => t.IsWild));
        Assert.Empty(used.Run.Stationery);
        Assert.DoesNotContain(used.Run.Deck, t => t.Id == 2 && t.IsWild); // the deck copy is unchanged
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0, 1 })]
    [InlineData(new[] { 42 })]
    public void FountainPen_NeedsExactlyOneHandTile(int[] tileIds)
    {
        Assert.False(RunRules.UseStationery(InRound(new FountainPen()), 0, Words, tileIds: tileIds).IsOk);
    }

    [Fact]
    public void FountainPen_CanBreakADeadlock()
    {
        var session = InRound(new FountainPen());

        Assert.True(StationeryCatalog.CanEscape(session.Run.Stationery, session.Round));
    }

    // ---------------------------------------------------------------- CLI

    [Fact]
    public void Cli_PlaysAWild_ForALetterNotInHand()
    {
        var placed = PlayCommandParser.ResolveTiles(Board.Empty(5), HandWithWild(),
            new PlayCommand(new Position(0, 0), Direction.Across, "CAT")).Value;

        Assert.Equal('A', placed[1].Tile.Letter.Char);
        Assert.True(placed[1].Tile.IsWild);
        Assert.True(PlacementValidator.Validate(Board.Empty(5), HandWithWild(), placed, Words).IsOk);
    }
}
