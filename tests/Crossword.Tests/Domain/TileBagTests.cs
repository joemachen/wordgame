using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Random;

namespace Crossword.Tests.Domain;

public class TileBagTests
{
    private static TileBag BagOf(string letters) =>
        new(letters.Select((c, i) => new Tile(i, Letter.From(c))).ToImmutableArray());

    [Fact]
    public void Draw_MovesTilesFromBagToResult()
    {
        var bag = BagOf("ABCDEFGH");

        var (drawn, remaining, _) = bag.Draw(3, Rng.FromSeed(1));

        Assert.Equal(3, drawn.Length);
        Assert.Equal(5, remaining.Count);
        Assert.Empty(drawn.Select(t => t.Id).Intersect(remaining.Tiles.Select(t => t.Id)));
    }

    [Fact]
    public void Draw_DoesNotMutateOriginalBag()
    {
        var bag = BagOf("ABCDEFGH");

        bag.Draw(3, Rng.FromSeed(1));

        Assert.Equal(8, bag.Count);
    }

    [Fact]
    public void Draw_MoreThanAvailable_DrawsEverything()
    {
        var (drawn, remaining, _) = BagOf("ABC").Draw(10, Rng.FromSeed(1));

        Assert.Equal(3, drawn.Length);
        Assert.True(remaining.IsEmpty);
    }

    [Fact]
    public void Draw_FromEmptyBag_ReturnsNothing()
    {
        var rng = Rng.FromSeed(1);

        var (drawn, remaining, nextRng) = TileBag.Empty.Draw(5, rng);

        Assert.Empty(drawn);
        Assert.True(remaining.IsEmpty);
        Assert.Equal(rng, nextRng);
    }

    [Fact]
    public void Draw_IsDeterministicForSameSeed()
    {
        var bag = BagOf("ABCDEFGHIJKLMNOP");

        var (a, _, _) = bag.Draw(5, Rng.FromSeed(99));
        var (b, _, _) = bag.Draw(5, Rng.FromSeed(99));

        Assert.Equal(a.Select(t => t.Id), b.Select(t => t.Id));
    }

    [Fact]
    public void Draw_RejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BagOf("A").Draw(-1, Rng.FromSeed(1)));
    }
}
