using System.IO.Compression;
using Crossword.Core.Lexicon;

namespace Crossword.Tests.Lexicon;

[Trait("Category", "Lexicon")]
public class DenylistTests
{
    private static readonly Lazy<HashSet<string>> Unfiltered = new(() =>
        LexiconLoader.ReadUnfilteredEnableWords().Select(w => w.Trim().ToUpperInvariant()).ToHashSet());

    private static Denylist List(params string[] lines) => Denylist.Read(new StringReader(string.Join("\n", lines)));

    [Fact]
    public void Read_SkipsCommentsAndBlankLinesAndNormalizesCase()
    {
        var list = List("# header", "", "  Foo  ", "BAR", "# baz");

        Assert.Equal(["BAR", "FOO"], list.Words.Order());
        Assert.True(list.Contains(" foo "));
        Assert.False(list.Contains("BAZ"));
    }

    [Fact]
    public void Read_RejectsEntriesThatAreNotWords()
    {
        Assert.Throws<FormatException>(() => List("two words"));
        Assert.Throws<FormatException>(() => List("x"));
    }

    [Fact]
    public void Filter_DropsDeniedWordsAndKeepsOrderAndSpelling()
    {
        Assert.Equal(["cat", "Dog"], List("foo").Filter(["cat", "FOO", "Dog", "foo"]));
    }

    [Fact]
    public void Default_IsNotEmpty()
    {
        Assert.NotEmpty(Denylist.Default.Words);
    }

    [Fact]
    public void Default_EveryEntryIsAnEnableWord()
    {
        // A typo would silently deny nothing.
        Assert.Empty(Denylist.Default.Words.Where(w => !Unfiltered.Value.Contains(w)));
    }

    [Fact]
    public void Default_ListsThePluralOfEveryDeniedWord()
    {
        // -ES only after O/S/X/Z/CH/SH: SPIC+ES is SPICES.
        var missing = Denylist.Default.Words
            .SelectMany(w => w.EndsWith('O') || w.EndsWith('S') || w.EndsWith('X') || w.EndsWith('Z') || w.EndsWith("CH") || w.EndsWith("SH")
                ? new[] { w + "S", w + "ES" }
                : [w + "S"])
            .Where(f => Unfiltered.Value.Contains(f) && !Denylist.Default.Contains(f))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Enable_RejectsEveryDeniedWord()
    {
        Assert.Empty(Denylist.Default.Words.Where(LexiconLoader.Enable.Contains));
        Assert.Empty(LexiconLoader.ReadEnableWords().Where(Denylist.Default.Contains));
    }

    [Theory]
    [InlineData("CRACKER")]
    [InlineData("GUINEA")]
    [InlineData("QUEER")]
    [InlineData("SHEENY")]
    [InlineData("JEW")]
    public void Enable_KeepsWordsWhoseMainMeaningIsInnocent(string word)
    {
        Assert.True(LexiconLoader.Enable.Contains(word));
    }

    [Fact]
    public void Definitions_NeverDefineDeniedWords()
    {
        Assert.Empty(Denylist.Default.Words.Where(w => DefinitionLoader.Default.Define(w) is not null));
    }

    [Fact]
    public void Definitions_NoGlossUsesADeniedWord()
    {
        using var stream = typeof(DefinitionLoader).Assembly.GetManifestResourceStream("Crossword.Core.Lexicon.definitions.tsv.gz")!;
        using var reader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
        var offending = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split('\t');
            if (line.StartsWith('#') || fields.Length != 5)
                continue;
            if (Denylist.Default.Contains(fields[0]) || Denylist.Default.Contains(fields[3])
                || fields[2].Split(GlossSeparators, StringSplitOptions.RemoveEmptyEntries).Any(w => char.IsLower(w[0]) && Denylist.Default.Contains(w)))
                offending.Add(line);
        }

        Assert.Empty(offending);
    }

    [Theory]
    [InlineData("CRACKER")]
    [InlineData("GUINEA")]
    [InlineData("PADDY")]
    public void Definitions_ShowACleanSenseOfWordsWithASlurSense(string word)
    {
        var definition = DefinitionLoader.Default.Define(word);

        Assert.NotNull(definition);
        Assert.DoesNotContain(definition.Senses, s => s.Gloss.Contains("slur", StringComparison.OrdinalIgnoreCase)
                                                    || s.Gloss.Contains("offensive", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly char[] GlossSeparators = [.. " \t,;:.!?()[]\"'/-–—…".ToCharArray()];
}
