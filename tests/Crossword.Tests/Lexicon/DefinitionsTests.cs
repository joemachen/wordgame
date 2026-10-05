using Crossword.Core.Lexicon;

namespace Crossword.Tests.Lexicon;

[Trait("Category", "Lexicon")]
public class DefinitionsTests
{
    private static DefinitionBook Book(params string[] lines) =>
        DefinitionBook.Read(new StringReader(string.Join("\n", lines)));

    private static readonly DefinitionBook Sample = Book(
        "# comment",
        "GLEY\tn\ta sticky clay soil\t\t",
        "GLEY\tv\tto turn into gley\t\t",
        "GLEYS\tn\t\tGLEY\tplural of",
        "GLEYED\tv\t\tGLEY\tpast tense of",
        "ORPHAN\tv\t\tNOWHERE\tpast tense of",
        "BROKEN LINE",
        "THE\tdet\tused before a known noun\t\t");

    [Fact]
    public void Define_ReturnsLemmaSensesInFileOrder()
    {
        var definition = Sample.Define("GLEY")!;

        Assert.Equal("GLEY", definition.Word);
        Assert.Equal(["n", "v"], definition.Senses.Select(s => s.PartOfSpeech));
        Assert.Null(definition.InflectionOf);
        Assert.Equal("n. a sticky clay soil", definition.Summary);
    }

    [Fact]
    public void Define_IsCaseInsensitive()
    {
        Assert.Equal("GLEY", Sample.Define(" gley ")!.Word);
    }

    [Fact]
    public void Define_FollowsInflectionToLemmaSensesOfThatPartOfSpeech()
    {
        var definition = Sample.Define("GLEYED")!;

        Assert.Equal("GLEY", definition.InflectionOf);
        Assert.Equal("past tense of", definition.Form);
        Assert.Equal(["v"], definition.Senses.Select(s => s.PartOfSpeech));
        Assert.Equal("past tense of GLEY: v. to turn into gley", definition.Summary);
    }

    [Fact]
    public void Define_ReturnsNullForUnknownWordsAndDanglingInflections()
    {
        Assert.Null(Sample.Define("ZZZ"));
        Assert.Null(Sample.Define("ORPHAN"));
    }

    [Fact]
    public void Read_SkipsCommentsAndMalformedLines()
    {
        Assert.Null(Sample.Define("BROKEN LINE"));
        Assert.Null(Sample.Define("# comment"));
        Assert.Equal(5, Sample.Count); // GLEY, GLEYS, GLEYED, ORPHAN, THE
    }

    [Fact]
    public void Summary_UsesReadableAbbreviationsForPartsOfSpeech()
    {
        Assert.Equal("det. used before a known noun", Sample.Define("THE")!.Summary);
        Assert.Equal("adj. quick", new WordSense("a", "quick").ToString());
        Assert.Equal("adv. quickly", new WordSense("r", "quickly").ToString());
    }

    [Theory]
    [InlineData("YULE", null)]
    [InlineData("HOUSE", null)]
    [InlineData("THE", null)]
    [InlineData("JUMPED", "JUMP")]
    [InlineData("WENT", "GO")]
    [InlineData("CHILDREN", "CHILD")]
    public void Embedded_DefinesCommonWordsAndTheirForms(string word, string? lemma)
    {
        var definition = DefinitionLoader.Default.Define(word);

        Assert.NotNull(definition);
        Assert.NotEmpty(definition.Senses);
        Assert.Equal(lemma, definition.InflectionOf);
    }

    [Fact]
    public void Embedded_DefinesEveryTwoLetterWord()
    {
        var undefined = LexiconLoader.ReadEnableWords()
            .Select(w => w.Trim())
            .Where(w => w.Length == 2 && DefinitionLoader.Default.Define(w) is null)
            .ToList();

        Assert.Empty(undefined);
    }
}
