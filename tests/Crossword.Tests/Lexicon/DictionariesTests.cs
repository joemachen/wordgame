using Crossword.Core.Lexicon;

namespace Crossword.Tests.Lexicon;

[Trait("Category", "Lexicon")]
public class DictionariesTests
{
    private static readonly string[] TechShorthand = [Dictionaries.TechShorthandId];

    public static IEnumerable<object[]> Ids() => Dictionaries.All.Select(d => new object[] { d.Id });

    [Fact]
    public void TheCatalog_HasUniqueIds_AndLooksUpCaseInsensitively()
    {
        Assert.Equal(Dictionaries.All.Length, Dictionaries.All.Select(d => d.Id).Distinct().Count());
        Assert.Same(Dictionaries.Get(Dictionaries.TechShorthandId), Dictionaries.Find("TECH-SHORTHAND"));
        Assert.Null(Dictionaries.Find("slang"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dictionaries.Get("slang"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void EveryEntry_IsANewWordOfTheMinimumLength_WithAnExpansion(string id)
    {
        var dictionary = Dictionaries.Get(id);
        var words = Dictionaries.Words(id).ToList();

        Assert.NotEmpty(words);
        Assert.All(words, word =>
        {
            Assert.Matches("^[A-Z]+$", word);
            Assert.InRange(word.Length, dictionary.MinLength, WordNormalizer.MaxLength);
            Assert.False(LexiconLoader.Enable.Contains(word), $"{word} is already an ENABLE word.");
            Assert.False(Denylist.Default.Contains(word), $"{word} is denied.");
            Assert.False(string.IsNullOrWhiteSpace(Dictionaries.Define(word, [id])?.Summary), $"{word} has no expansion.");
        });
    }

    [Fact]
    public void Read_SkipsComments_ShortDeniedAndRepeatedEntries()
    {
        var denylist = Denylist.Read(new StringReader("BADWORD"));
        const string data = "# comment\n\nCPU\tcentral processing unit\nTV\ttelevision\nBADWORD\tdenied\n"
            + "cpu\tagain\nNASA\t  space agency  \nX-RAY\tnot a word\n";

        var entries = Dictionaries.Read(new StringReader(data), minLength: 3, denylist);

        Assert.Equal(["CPU", "NASA"], entries.Keys.Order());
        Assert.Equal("central processing unit", entries["CPU"]); // the first entry wins
        Assert.Equal("space agency", entries["NASA"]);
    }

    [Fact]
    public void Define_GivesTheExpansion_OnlyFromTheDictionariesAsked()
    {
        var definition = Dictionaries.Define("cpu", TechShorthand);

        Assert.NotNull(definition);
        Assert.Equal("CPU", definition.Word);
        Assert.Equal("abbr. central processing unit", definition.Summary);
        Assert.Null(Dictionaries.Define("CPU", []));
        Assert.Null(Dictionaries.Define("CAT", TechShorthand));
    }

    [Fact]
    public void LexiconFor_NoDictionaries_IsEnable()
    {
        Assert.Same(LexiconLoader.Enable, LexiconLoader.For([]));
        Assert.False(LexiconLoader.Enable.Contains("CPU"));
    }

    [Fact]
    public void LexiconFor_MergesEnableAndTheOverlay_AndIsShared()
    {
        var lexicon = LexiconLoader.For(TechShorthand);

        Assert.True(lexicon.Contains("CPU"));
        Assert.True(lexicon.Contains("nasa"));
        Assert.True(lexicon.Contains("CAT"));
        Assert.False(lexicon.Contains("TV")); // too short for the overlay, not an ENABLE word
        Assert.Equal(LexiconLoader.Enable.WordCount + Dictionaries.Words(Dictionaries.TechShorthandId).Count(), lexicon.WordCount);
        Assert.Same(lexicon, LexiconLoader.For(["TECH-SHORTHAND", Dictionaries.TechShorthandId]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LexiconLoader.For(["slang"]));
    }
}
