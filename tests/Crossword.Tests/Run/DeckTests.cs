using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Tests.Run;

[Trait("Category", "Run")]
public class DeckTests
{
    private static readonly DeckConfig Numbers = new(
        CrosswordDraftIntersectionBonus: 2, CrosswordDraftMinWordLength: 4, CrosswordDraftTargetScale: 0.5m, RedactorDiscardsDelta: -2,
        CopyEditorDiscardsDelta: 3, CopyEditorDeskSlots: 3, CopyEditorStartingItem: "red-pen", LexicographerDeskSlots: 2, LexicographerTargetScale: 0.8m);

    private static readonly RunConfig Base = RunConfig.Default with
    {
        Scoring = ScoringConfig.Default with { IntersectionMult = 5 },
    };

    [Fact]
    public void Standard_IsTheBaseGame() =>
        Assert.Same(Base, Decks.Apply(Base, Decks.StandardId, Numbers));

    [Fact]
    public void TheCatalog_StartsWithStandard_AndHasUniqueIds()
    {
        Assert.Equal(Decks.StandardId, Decks.All[0].Id);
        Assert.Equal(Decks.All.Length, Decks.All.Select(d => d.Id).Distinct().Count());
        Assert.Same(Decks.Get(Decks.RedactorId), Decks.Find("REDACTOR"));
        Assert.Null(Decks.Find("tabloid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Decks.Apply(Base, "tabloid"));
    }

    [Fact]
    public void CrosswordDraft_AddsIntersectionMult_RaisesMinLength_CutsDeadlines_AndDropsTheGrammarian()
    {
        var config = Decks.Apply(Base, Decks.CrosswordDraftId, Numbers);

        Assert.Equal(7, config.Scoring.IntersectionMult);
        Assert.Equal(Base.WeekTargets.Select(t => (long)Math.Round(t * 0.5m / 10m, MidpointRounding.AwayFromZero) * 10), config.WeekTargets);
        Assert.Equal(260, Decks.Apply(Base with { WeekTargets = [515] }, Decks.CrosswordDraftId, Numbers).WeekTargets[0]); // 257.5 → 260
        Assert.Equal(4, config.RoundConfigFor(0).MinWordLength);
        for (int week = 0; week < 8; week++) // every tier and endless weeks
            Assert.DoesNotContain(config.BossPoolFor(week), boss => boss is StrictGrammarian);
        Assert.NotEmpty(config.BossPoolFor(4)); // the Final tier keeps The Puzzle Master
    }

    [Fact]
    public void MinWordLength_IsTheHigherOfTheRunsAndTheBosss()
    {
        var config = Base with { MinWordLength = 3 };

        Assert.Equal(3, config.RoundConfigFor(2, new InkSpill()).MinWordLength);
        Assert.Equal(5, config.RoundConfigFor(2, new StrictGrammarian(MinLength: 5)).MinWordLength);
        Assert.Equal(2, Base.RoundConfigFor(0).MinWordLength);
    }

    [Fact]
    public void CrosswordDraft_BossesStayDeterministic_AndPreviewable()
    {
        var session = RunRules.NewGame(11, Base, LexiconLoader.Enable, run => run with { RoundIndex = 14 }, deck: Decks.CrosswordDraftId);

        Assert.IsType<PuzzleMaster>(session.Round.Config.Boss); // the only Final boss left
        Assert.Equal(session.WeekBoss, RunRules.BossFor(session.Config, session.Run, 4));
        Assert.True(session.Round.Config.MinWordLength >= 3);
    }

    [Fact]
    public void Redactor_StartsWithTheThinDeck_AndOneFewerDiscard()
    {
        var config = Decks.Apply(Base, Decks.RedactorId, Numbers);

        Assert.Equal(StartingDeck.Thin().Length, config.StartingTiles.Length);
        Assert.Equal(-2, config.DiscardsDelta);
        Assert.Equal(new RoundConfig(0).Discards - 2, config.RoundConfigFor(0).Discards);
    }

    [Fact]
    public void ThinDeck_Is30Tiles_WithAFairVowelShare_AndTheRareLetters()
    {
        var tiles = StartingDeck.Thin();
        var lettered = tiles.Where(t => !t.IsWild).ToList();

        Assert.Equal(ShopConfig.Default.MinDeckSize, tiles.Length);
        Assert.Equal(StartingDeck.ThinWilds, tiles.Count(t => t.IsWild));
        Assert.InRange(lettered.Count(t => "AEIOU".Contains(t.Letter.Char)) / (double)lettered.Count, 0.38, 0.45);
        Assert.All("QZXJ", rare => Assert.Contains(lettered, t => t.Letter.Char == rare));
        Assert.Equal(tiles.Length, tiles.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void CopyEditor_StartsWithRedPen_MoreDiscards_AndFewerSlots()
    {
        var config = Decks.Apply(Base, Decks.CopyEditorId, Numbers);

        Assert.Equal(3, config.DeskSlots);
        Assert.Equal(3, config.DiscardsDelta);
        Assert.Equal(["red-pen"], config.StartingDeskItems.Select(i => i.Id));
    }

    [Fact]
    public void CopyEditor_NewGame_HoldsRedPen_AndRefusesAFifthItem()
    {
        var session = RunRules.NewGame(4, Base, LexiconLoader.Enable, deck: Decks.CopyEditorId);
        var deck = DeckConfig.Default;

        Assert.Equal(Decks.CopyEditorId, session.Run.DeckId);
        Assert.Equal(["red-pen"], session.Run.DeskItems.Select(i => i.Id));
        Assert.Equal(new RoundConfig(0).Discards + deck.CopyEditorDiscardsDelta, session.Round.DiscardsLeft);

        var run = session.Run;
        foreach (var item in DeskItemCatalog.All.Where(i => i.Id != "red-pen").Take(deck.CopyEditorDeskSlots - 1))
            run = run.AddDeskItem(item, session.Config.DeskSlots).Value;
        Assert.Equal(deck.CopyEditorDeskSlots, run.DeskItems.Length);
        Assert.False(run.AddDeskItem(DeskItemCatalog.All.Last(), session.Config.DeskSlots).IsOk);
    }

    [Fact]
    public void Lexicographer_HasFewerSlots_CutsDeadlines_AndOnlyItTakesADictionary()
    {
        var config = Decks.Apply(Base, Decks.LexicographerId, Numbers);
        Assert.Equal(2, config.DeskSlots);
        Assert.Equal(Base.WeekTargets.Select(t => (long)Math.Round(t * 0.8m / 10m, MidpointRounding.AwayFromZero) * 10), config.WeekTargets);
        Assert.True(Decks.TakesDictionary(Decks.LexicographerId));
        Assert.All(Decks.All.Where(d => d.Id != Decks.LexicographerId), d => Assert.False(Decks.TakesDictionary(d.Id)));
        Assert.Empty(Decks.DictionariesFor(Decks.StandardId, null));
        Assert.Throws<ArgumentException>(() => Decks.DictionariesFor(Decks.RedactorId, Dictionaries.TechShorthandId));
        Assert.Throws<ArgumentOutOfRangeException>(() => Decks.DictionariesFor(Decks.LexicographerId, "slang"));
    }

    [Fact]
    public void Lexicographer_NewGame_StoresThePickedDictionary_OrTheFirst()
    {
        var picked = RunRules.NewGame(4, Base, LexiconLoader.Enable, deck: Decks.LexicographerId, dictionary: "TECH-SHORTHAND");
        var defaulted = RunRules.NewGame(4, Base, LexiconLoader.Enable, deck: Decks.LexicographerId);

        Assert.Equal([Dictionaries.TechShorthandId], picked.Run.Dictionaries);
        Assert.Equal([Dictionaries.All[0].Id], defaulted.Run.Dictionaries);
        Assert.Equal(DeckConfig.Default.LexicographerDeskSlots, picked.Config.DeskSlots);
        Assert.Empty(RunRules.NewGame(4, Base, LexiconLoader.Enable).Run.Dictionaries);
        Assert.Throws<ArgumentException>(() => RunRules.NewGame(4, Base, LexiconLoader.Enable, deck: Decks.CopyEditorId,
            dictionary: Dictionaries.TechShorthandId));
    }

    [Fact]
    public void AnOverlayWord_IsLegal_OnlyOnTheRunsWordGraph()
    {
        var session = RunRules.NewGame(4, Base, LexiconLoader.Enable, deck: Decks.LexicographerId);
        var hand = new Hand([new Tile(0, Letter.From('C')), new Tile(1, Letter.From('P')), new Tile(2, Letter.From('U'))]);
        var placed = new[] { 'C', 'P', 'U' }.Select((_, i) => new PlacedTile(new Position(3, 2 + i), hand.Tiles[i])).ToList();
        var board = Board.Empty(7);

        Assert.True(PlacementValidator.Validate(board, hand, placed, LexiconLoader.For(session.Run.Dictionaries)).IsOk);
        Assert.False(PlacementValidator.Validate(board, hand, placed, LexiconLoader.Enable).IsOk);
    }

    [Fact]
    public void DeckThenPressRun_Compose()
    {
        // Redactor (−1 discard) at Ink Shortage (−1 discard): 3 − 2 = 1 discard.
        var session = RunRules.NewGame(6, RunConfig.Default, LexiconLoader.Enable, pressRun: 5, deck: Decks.RedactorId);

        Assert.Equal(new RoundConfig(0).Discards + DeckConfig.Default.RedactorDiscardsDelta + PressRunConfig.Default.DiscardsDelta,
            session.Round.DiscardsLeft);
        Assert.Equal(StartingDeck.Thin().Length, session.Run.Deck.Length);
        Assert.Equal((5, Decks.RedactorId), (session.Run.PressRun, session.Run.DeckId));
    }

    [Fact]
    public void NewGame_DefaultsToTheStandardDeck()
    {
        var session = RunRules.NewGame(5, RunConfig.Default, LexiconLoader.Enable);

        Assert.Equal(Decks.StandardId, session.Run.DeckId);
        Assert.Same(RunConfig.Default, session.Config);
        Assert.Empty(session.Run.DeskItems);
    }

    [Theory]
    [InlineData(Decks.CrosswordDraftId)]
    [InlineData(Decks.RedactorId)]
    [InlineData(Decks.CopyEditorId)]
    [InlineData(Decks.LexicographerId)]
    public void EveryDeck_SameSeed_PlaysTheSame(string deck)
    {
        var a = RunRules.NewGame(8, RunConfig.Default, LexiconLoader.Enable, deck: deck);
        var b = RunRules.NewGame(8, RunConfig.Default, LexiconLoader.Enable, deck: deck);

        Assert.Equal(a.Round.Hand.Tiles.Select(t => t.Id), b.Round.Hand.Tiles.Select(t => t.Id));
    }
}
