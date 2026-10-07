using Crossword.Core.Lexicon;
using Crossword.Core.Random;

namespace Crossword.Tests.Lexicon;

[Trait("Category", "Lexicon")]
public class EnableLexiconTests
{
    private static readonly Lazy<string[]> EnableWords = new(() =>
        LexiconLoader.ReadEnableWords()
            .Select(w => WordNormalizer.TryNormalize(w, out var n) ? n : null)
            .OfType<string>()
            .Distinct()
            .ToArray());

    private static readonly Lazy<HashSetLexicon> Reference = new(() => new HashSetLexicon(EnableWords.Value));

    [Theory]
    [InlineData("AA")]
    [InlineData("CRANE")]
    [InlineData("QUIZ")]
    [InlineData("crossword")]
    public void Enable_ContainsCommonWords(string word)
    {
        Assert.True(LexiconLoader.Enable.Contains(word));
    }

    [Theory]
    [InlineData("XQZ")]
    [InlineData("CRANEE")]
    [InlineData("QI")] // TWL06 addition, not in ENABLE
    public void Enable_RejectsNonWords(string word)
    {
        Assert.False(LexiconLoader.Enable.Contains(word));
    }

    [Fact]
    public void Enable_LoadsExpectedWordCount()
    {
        // ENABLE has 172,823 entries; 168,551 remain after filtering to lengths 2..15, 168,423 without the 128 denied words.
        Assert.Equal(168_423, LexiconLoader.Enable.WordCount);
        Assert.Equal(EnableWords.Value.Length, LexiconLoader.Enable.WordCount);
    }

    [Fact]
    public void Dawg_AgreesWithHashSet_OnEveryEnableWord()
    {
        var dawg = LexiconLoader.Enable;
        var missing = EnableWords.Value.Where(w => !dawg.Contains(w)).Take(5).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Dawg_AgreesWithHashSet_OnMutatedNonWordsAndPrefixes()
    {
        var dawg = LexiconLoader.Enable;
        var words = EnableWords.Value;
        var rng = Rng.FromSeed(2026);

        for (int i = 0; i < 20_000; i++)
        {
            (int wi, rng) = rng.NextInt(words.Length);
            (int pos, rng) = rng.NextInt(words[wi].Length);
            (int letter, rng) = rng.NextInt(26);
            var chars = words[wi].ToCharArray();
            chars[pos] = (char)('A' + letter);
            string candidate = new(chars);
            string prefix = words[wi][..(pos + 1)] + (char)('A' + letter);

            Assert.Equal(Reference.Value.Contains(candidate), dawg.Contains(candidate));
            Assert.Equal(Reference.Value.HasPrefix(prefix), dawg.HasPrefix(prefix));
        }
    }

    [Fact]
    public void Dawg_IsSubstantiallySmallerThanTrie()
    {
        var dawg = (Dawg)LexiconLoader.Enable;

        Assert.True(dawg.NodeCount * 3 < DawgTests.TrieNodeCount(EnableWords.Value),
            $"DAWG nodes: {dawg.NodeCount}");
    }
}
