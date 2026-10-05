using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Rules;
using Crossword.Core.Run;
using static Crossword.Tests.TestSupport.Fixtures;

namespace Crossword.Tests.Rules;

public class BalancedDrawTests
{
    private static readonly DrawConfig Balanced = new(MinVowels: 2, MinConsonants: 2, MaxCopiesPerVowel: 2);

    private static TileBag BagOf(string letters, int firstId = 100) =>
        new(letters.Select((c, i) => new Tile(firstId + i, Letter.From(c))).ToImmutableArray());

    private static int Vowels(IEnumerable<Tile> tiles) => tiles.Count(DrawConfig.IsVowel);

    private static RoundState Deal(ulong seed, DrawConfig? draw) =>
        DrawRules.DrawToHandSize(new RoundState(new RoundConfig(TargetScore: 100, Draw: draw), Board.Empty(7),
            new TileBag(StartingDeck.Create()), Hand.Empty, Rng.FromSeed(seed), Score: 0, SubmissionsLeft: 4, DiscardsLeft: 3));

    [Fact]
    public void BalancedDeal_AlwaysHasTwoVowelsTwoConsonants_AndNoTripleVowel()
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var hand = Deal(seed, Balanced).Hand.Tiles;

            Assert.Equal(7, hand.Length);
            Assert.InRange(Vowels(hand), 2, 5);
            Assert.True(hand.Count(DrawConfig.IsConsonant) >= 2);
            Assert.All(hand.Where(DrawConfig.IsVowel).GroupBy(t => t.Letter), g => Assert.True(g.Count() <= 2));
        }
    }

    [Fact]
    public void Refill_TopsUpVowels_WhenTheKeptTilesAreAllConsonants()
    {
        var bag = BagOf("BCDFGHJKLMNPRSTAE"); // only two vowels, at the end
        var (drawn, remaining, _) = bag.DrawBalanced(2, HandOf("QZXVW").Tiles, Balanced, Rng.FromSeed(3));

        Assert.Equal(2, Vowels(drawn));
        Assert.Equal(bag.Count - 2, remaining.Count);
    }

    [Fact]
    public void BalancedDraw_IsBestEffort_WhenTheBagCantSatisfyIt()
    {
        var (drawn, remaining, _) = BagOf("BCDFGHA").DrawBalanced(7, [], Balanced, Rng.FromSeed(1));

        Assert.Equal(7, drawn.Length);
        Assert.Equal(1, Vowels(drawn));
        Assert.True(remaining.IsEmpty);
    }

    [Fact]
    public void CopyCap_SkipsAThirdCopyOfAVowel_WhileOthersRemain()
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var (drawn, _, _) = BagOf("EEEEEEEEEEST").DrawBalanced(2, HandOf("EERT").Tiles, Balanced, Rng.FromSeed(seed));

            Assert.Equal(["S", "T"], drawn.Select(t => t.Letter.ToString()).Order());
        }
    }

    [Fact]
    public void DrawsOff_MatchThePlainDraw()
    {
        var plain = Deal(42, null);
        var off = Deal(42, DrawConfig.Off);
        var (drawn, _, rng) = new TileBag(StartingDeck.Create()).Draw(7, Rng.FromSeed(42));

        Assert.Equal(drawn.Select(t => t.Id), plain.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(drawn.Select(t => t.Id), off.Hand.Tiles.Select(t => t.Id));
        Assert.Equal(rng, plain.Rng);
    }

    [Fact]
    [Trait("Category", "Determinism")]
    public void BalancedDraw_IsDeterministic()
    {
        Assert.Equal(Deal(9, Balanced).Hand.Tiles.Select(t => t.Id), Deal(9, Balanced).Hand.Tiles.Select(t => t.Id));
    }

    [Fact]
    public void Runs_DrawBalanced_ByDefault()
    {
        Assert.Equal(DrawConfig.Balanced, RunConfig.Default.RoundConfigFor(0).Draw);
        Assert.Equal(DrawConfig.Balanced, RunConfig.Default.RoundConfigFor(2, new InkSpill()).Draw); // bosses keep it
    }

    [Fact]
    public void StartingDeck_Has98Letters_41Vowels_AndTwoWilds()
    {
        var deck = StartingDeck.Create();

        Assert.Equal(100, deck.Length);
        Assert.Equal(41, Vowels(deck));
        Assert.Equal(2, deck.Count(t => t.IsWild));
    }
}
