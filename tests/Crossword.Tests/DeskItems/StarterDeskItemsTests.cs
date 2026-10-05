using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

[Trait("Category", "Scoring")]
public class StarterDeskItemsTests
{
    // CAT across on an empty board: 1 word, longest 3, no intersections, no rare letters.
    private static readonly PlayAnalysis SimplePlay = PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT");

    // TO under AT in CAT: words TO, AT, TO — 3 words, 2 intersections.
    private static readonly PlayAnalysis ParallelPlay =
        PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO");

    // ZAX across on an empty board: two rare letters (Z, X).
    private static readonly PlayAnalysis RarePlay = PlayOn(Board.Empty(5), "ZAX", 0, 0, Direction.Across, "ZAX");

    private static ScoreContext Start(PlayAnalysis play) => ScoreContext.Start(play, chips: 10, mult: 2);

    private static void AssertNoEffect(IDeskItem item, PlayAnalysis play)
    {
        var start = Start(play);
        Assert.Same(start, item.Apply(start));
    }

    [Fact]
    public void RedPen_AddsFlatMult_AndLogs()
    {
        var result = new RedPen(Mult: 4).Apply(Start(SimplePlay));

        Assert.Equal(6m, result.Mult);
        Assert.Equal("red-pen", Assert.Single(result.Log).SourceId);
    }

    [Fact]
    public void Thesaurus_AddsChipsPerLetterOfLongestWord()
    {
        Assert.Equal(10 + 9, new Thesaurus(ChipsPerLetter: 3).Apply(Start(SimplePlay)).Chips);
    }

    [Fact]
    public void Inkwell_AddsChipsPerExtraWord()
    {
        Assert.Equal(10 + 24, new Inkwell(ChipsPerExtraWord: 12).Apply(Start(ParallelPlay)).Chips);
    }

    [Fact]
    public void Inkwell_SingleWord_HasNoEffect() => AssertNoEffect(new Inkwell(), SimplePlay);

    [Fact]
    public void CrossReference_MultipliesMult_WithEnoughIntersections()
    {
        Assert.Equal(3m, new CrossReference(MinIntersections: 2, Factor: 1.5m).Apply(Start(ParallelPlay)).Mult);
    }

    [Fact]
    public void CrossReference_TooFewIntersections_HasNoEffect() => AssertNoEffect(new CrossReference(), SimplePlay);

    [Fact]
    public void Broadsheet_MultipliesMult_ForLongWords()
    {
        Assert.Equal(4m, new Broadsheet(MinLength: 3, Factor: 2).Apply(Start(SimplePlay)).Mult);
    }

    [Fact]
    public void Broadsheet_ShortWord_HasNoEffect() => AssertNoEffect(new Broadsheet(MinLength: 4), SimplePlay);

    [Fact]
    public void RareInk_AddsMultPerRareTilePlaced()
    {
        Assert.Equal(2m + 6m, new RareInk(MultPerTile: 3).Apply(Start(RarePlay)).Mult);
    }

    [Fact]
    public void RareInk_NoRareTiles_HasNoEffect() => AssertNoEffect(new RareInk(), SimplePlay);

    [Fact]
    public void Catalog_HasUniqueIds_AndFindIsCaseInsensitive()
    {
        Assert.Equal(DeskItemCatalog.All.Length, DeskItemCatalog.All.Select(i => i.Id).Distinct().Count());
        Assert.IsType<RedPen>(DeskItemCatalog.Find("RED-PEN"));
        Assert.Null(DeskItemCatalog.Find("nope"));
    }

    [Fact]
    public void Catalog_ItemsAreDocumented()
    {
        Assert.All(DeskItemCatalog.All, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Name));
            Assert.False(string.IsNullOrWhiteSpace(i.Description));
        });
    }
}
