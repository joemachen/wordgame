using Crossword.Core.Lexicon;

namespace Crossword.Tests.Lexicon;

[Trait("Category", "Lexicon")]
public class DawgTests
{
    private static readonly string[] Words = ["CAT", "CATS", "CAR", "CARS", "DOG", "DOGS", "AT", "TAPS", "TOPS"];

    public static TheoryData<string> LexiconKinds => new() { "dawg", "hashset" };

    private static ILexicon Create(string kind, IEnumerable<string> words) =>
        kind == "dawg" ? Dawg.Build(words) : new HashSetLexicon(words);

    [Theory]
    [MemberData(nameof(LexiconKinds))]
    public void Contains_FindsEveryInsertedWord(string kind)
    {
        var lexicon = Create(kind, Words);

        Assert.All(Words, w => Assert.True(lexicon.Contains(w), w));
        Assert.Equal(Words.Length, lexicon.WordCount);
    }

    [Theory]
    [MemberData(nameof(LexiconKinds))]
    public void Contains_RejectsPrefixesAndUnknownWords(string kind)
    {
        var lexicon = Create(kind, Words);

        Assert.False(lexicon.Contains("CA"));
        Assert.False(lexicon.Contains("DO"));
        Assert.False(lexicon.Contains("CATSS"));
        Assert.False(lexicon.Contains("BAT"));
        Assert.False(lexicon.Contains(""));
    }

    [Theory]
    [MemberData(nameof(LexiconKinds))]
    public void Contains_IsCaseInsensitive(string kind)
    {
        var lexicon = Create(kind, ["crane"]);

        Assert.True(lexicon.Contains("CRANE"));
        Assert.True(lexicon.Contains("Crane"));
    }

    [Theory]
    [MemberData(nameof(LexiconKinds))]
    public void HasPrefix_DistinguishesLivePrefixes(string kind)
    {
        var lexicon = Create(kind, Words);

        Assert.True(lexicon.HasPrefix("CA"));
        Assert.True(lexicon.HasPrefix("CATS"));
        Assert.True(lexicon.HasPrefix("T"));
        Assert.False(lexicon.HasPrefix("CATZ"));
        Assert.False(lexicon.HasPrefix("X"));
    }

    [Theory]
    [MemberData(nameof(LexiconKinds))]
    public void Build_SkipsInvalidEntries_AndDeduplicates(string kind)
    {
        var lexicon = Create(kind, ["cat", "CAT", " cat ", "a", "c4t", "", "toolongforanyboardword"]);

        Assert.Equal(1, lexicon.WordCount);
        Assert.True(lexicon.Contains("CAT"));
    }

    [Fact]
    public void Build_SharesCommonSuffixes()
    {
        // CATS/CARS/DOGS/TAPS/TOPS all end in "S"; a trie would duplicate those tail nodes.
        var dawg = Dawg.Build(Words);

        Assert.True(dawg.NodeCount < TrieNodeCount(Words));
    }

    [Fact]
    public void Build_EmptyInput_ProducesEmptyLexicon()
    {
        var dawg = Dawg.Build([]);

        Assert.Equal(0, dawg.WordCount);
        Assert.False(dawg.Contains("A"));
        Assert.False(dawg.HasPrefix(""));
    }

    internal static int TrieNodeCount(IEnumerable<string> words)
    {
        var prefixes = new HashSet<string> { "" };
        foreach (var w in words)
        {
            for (int i = 1; i <= w.Length; i++)
                prefixes.Add(w[..i]);
        }
        return prefixes.Count;
    }
}
