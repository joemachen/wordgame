using Crossword.Core.Domain;
using Crossword.Core.Random;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Domain;

public class HandArrangementTests
{
    [Fact]
    public void Reconcile_EmptyOrder_UsesHandOrder()
    {
        Assert.Equal([0, 1, 2], HandArrangement.Reconcile([], HandOf("CAT")));
    }

    [Fact]
    public void Reconcile_KeepsPlayerOrder_DropsGoneTiles_AppendsNewOnes()
    {
        // Player arranged 2,0,1; tile 1 was played and tiles 7, 8 drawn.
        var hand = new Hand([new Tile(0, Letter.From('C')), new Tile(2, Letter.From('T')),
            new Tile(7, Letter.From('E')), new Tile(8, Letter.From('S'))]);

        Assert.Equal([2, 0, 7, 8], HandArrangement.Reconcile([2, 0, 1], hand));
    }

    [Theory]
    [InlineData(0, 2, false, new[] { 1, 0, 2, 3 })] // before
    [InlineData(0, 2, true, new[] { 1, 2, 0, 3 })]  // after
    [InlineData(3, 0, false, new[] { 3, 0, 1, 2 })] // to the front
    [InlineData(0, 3, true, new[] { 1, 2, 3, 0 })]  // to the end
    public void Move_PlacesTileBeforeOrAfterTarget(int tile, int target, bool after, int[] expected)
    {
        Assert.Equal(expected, HandArrangement.Move([0, 1, 2, 3], tile, target, after));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(1, 9)]
    public void Move_SameOrUnknownTile_LeavesOrderUnchanged(int tile, int target)
    {
        Assert.Equal([0, 1, 2], HandArrangement.Move([0, 1, 2], tile, target, after: false));
    }

    [Fact]
    public void Shuffle_IsAPermutation_ThatAlwaysChangesOrder()
    {
        int[] order = [4, 5, 6];
        var rng = Rng.FromSeed(1);
        for (int i = 0; i < 50; i++)
        {
            (var shuffled, rng) = HandArrangement.Shuffle(order, rng);
            Assert.NotEqual(order, shuffled.ToArray());
            Assert.Equal(order, shuffled.Order());
        }
    }

    [Fact]
    public void Shuffle_SingleTile_IsUnchanged()
    {
        Assert.Equal([4], HandArrangement.Shuffle([4], Rng.FromSeed(1)).Order);
    }
}
