using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

/// <summary>Every Desk Item says what it does to the score, so the desk card can show why its slot matters.</summary>
[Trait("Category", "Scoring")]
public class DeskItemKindTests
{
    [Theory]
    [InlineData("red-pen", DeskItemKind.Mult)]
    [InlineData("thesaurus", DeskItemKind.Chips)]
    [InlineData("pulitzer", DeskItemKind.TimesMult)]
    [InlineData("second-printing", DeskItemKind.TimesChipsAndMult)]
    [InlineData("syndication", DeskItemKind.Money)]
    [InlineData("tile-rack", DeskItemKind.Rules)]
    public void Catalog_TagsItemsByWhatTheyDo(string id, DeskItemKind kind) => Assert.Equal(kind, DeskItemCatalog.Find(id)!.Kind);

    [Fact]
    public void EveryKind_HasATagAndAnExplanation()
    {
        foreach (var kind in Enum.GetValues<DeskItemKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DeskItemKinds.Tag(kind)));
            Assert.False(string.IsNullOrWhiteSpace(DeskItemKinds.Explain(kind)));
        }
        Assert.Equal("×MULT", DeskItemKinds.Tag(DeskItemKind.TimesMult));
    }

    /// <summary>
    /// The tag matches the behaviour: on probe plays, an item tagged ×Mult never adds chips and only ever scales the
    /// mult it was given; a +Chips or +Mult item never touches the other number. (Conditional items that don't fire
    /// on a probe leave it unchanged, which passes.)
    /// </summary>
    [Fact]
    public void Tags_MatchWhatTheItemDoesToTheScore()
    {
        PlayAnalysis[] plays =
        [
            PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT"),
            PlayOn(Board.Empty(5), "STAR", 0, 0, Direction.Across, "STAR"),
            PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO"),
        ];
        ScoreEnvironment[] envs =
        [
            new(20, 1, 0) { SubmissionsMade = 0 },
            new(20, 4, 3) { SubmissionsMade = 2 },
        ];
        foreach (var item in DeskItemCatalog.All)
        {
            foreach (var play in plays)
            {
                foreach (var env in envs)
                {
                    var start = ScoreContext.Start(play, chips: 10, mult: 2) with { Env = env, WordChips = [.. play.Words.Select(_ => 5L)] };
                    var after = item.Apply(start);
                    var doubled = item.Apply(start with { Mult = 4 }); // the same play with twice the mult going in
                    switch (item.Kind)
                    {
                        case DeskItemKind.Chips:
                            Assert.True(after.Mult == start.Mult && after.Chips >= start.Chips, item.Id);
                            break;
                        case DeskItemKind.Mult: // adds the same amount whatever mult it is given
                            Assert.True(after.Chips == start.Chips && after.Mult - 2 == doubled.Mult - 4 && after.Mult >= 2, item.Id);
                            break;
                        case DeskItemKind.TimesMult: // scales the mult it is given
                            Assert.True(after.Chips == start.Chips && after.Mult / 2 == doubled.Mult / 4, item.Id);
                            break;
                        case DeskItemKind.Money or DeskItemKind.Rules:
                            Assert.True(after.Chips == start.Chips && after.Mult == start.Mult, item.Id);
                            break;
                    }
                }
            }
        }
    }
}
