using Crossword.Core.DeskItems;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.DeskItems;

/// <summary>The automatic Desk Item order is the best one for any play, so the player never has to order cards.</summary>
[Trait("Category", "Scoring")]
public class DeskOrderTests
{
    private static IEnumerable<IEnumerable<T>> Permutations<T>(IReadOnlyList<T> items) =>
        items.Count <= 1
            ? [items]
            : items.SelectMany((item, i) => Permutations(items.Where((_, j) => j != i).ToList()).Select(rest => rest.Prepend(item)));

    [Fact]
    public void Sorted_PutsChipsThenMultThenMultipliers()
    {
        IDeskItem[] items = [new SecondPrinting(), new Pulitzer(), new TileRack(), new RedPen(), new Thesaurus()];

        Assert.Equal(["thesaurus", "red-pen", "tile-rack", "pulitzer", "second-printing"], DeskOrder.Sorted(items).Select(i => i.Id));
    }

    /// <summary>
    /// For loadouts drawn from the whole catalog, on a few plays and round situations, no permutation of the desk
    /// scores more than the sorted one.
    /// </summary>
    [Fact]
    public void Sorted_ScoresAtLeastAsMuchAsAnyOtherOrder()
    {
        PlayAnalysis[] plays =
        [
            PlayOn(Board.Empty(5), "CAT", 0, 0, Direction.Across, "CAT"),
            PlayOn(Board.Empty(5), "STAR", 0, 0, Direction.Across, "STAR"),
            PlayOn(BoardFromRows("CAT..", ".....", ".....", ".....", "....."), "TO", 1, 1, Direction.Across, "TO"),
        ];
        ScoreEnvironment[] envs = [new(20, 1, 0) { SubmissionsMade = 3 }, new(8, 4, 3) { SubmissionsMade = 0 }];
        var rng = Rng.FromSeed(11);
        for (int loadout = 0; loadout < 12; loadout++)
        {
            var pool = DeskItemCatalog.All.ToList();
            var desk = new List<IDeskItem>();
            for (int i = 0; i < 5; i++)
            {
                (int pick, rng) = rng.NextInt(pool.Count);
                desk.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            var sorted = DeskOrder.Sorted(desk);
            foreach (var play in plays)
            {
                foreach (var env in envs)
                {
                    long best = ScoringEngine.Score(play, sorted, ScoringConfig.Default, env).Total;
                    foreach (var order in Permutations(desk))
                        Assert.True(ScoringEngine.Score(play, order.ToList(), ScoringConfig.Default, env).Total <= best,
                            $"{string.Join(", ", order.Select(i => i.Id))} beats the sorted desk on {play.LongestWord.Text}");
                }
            }
        }
    }
}
