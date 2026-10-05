using System.Collections.Immutable;
using System.IO.Compression;

namespace Crossword.Core.Lexicon;

/// <summary>One sense of a word: part of speech (n, v, a, r, prep, pron, conj, interj, det) and a short gloss.</summary>
public sealed record WordSense(string PartOfSpeech, string Gloss)
{
    public string PosAbbreviation => PartOfSpeech switch
    {
        "a" => "adj.",
        "r" => "adv.",
        _ => PartOfSpeech + ".",
    };

    public override string ToString() => $"{PosAbbreviation} {Gloss}";
}

/// <summary>
/// A word's definition. Inflected forms carry the lemma they come from (GLEYED → GLEY, "past tense of") and the
/// lemma's senses for that part of speech.
/// </summary>
public sealed record WordDefinition(string Word, ImmutableArray<WordSense> Senses, string? InflectionOf = null, string? Form = null)
{
    /// <summary>One line for the UI: "n. a sticky clay soil" or "past tense of GLEY: v. …".</summary>
    public string Summary
    {
        get
        {
            string sense = Senses.IsDefaultOrEmpty ? "" : Senses[0].ToString();
            return InflectionOf is null ? sense : $"{Form} {InflectionOf}: {sense}".TrimEnd(':', ' ');
        }
    }
}

/// <summary>
/// Word definitions keyed by uppercase word. Data lines are tab-separated
/// <c>WORD, pos, gloss, lemma, form</c>: a lemma line has a gloss (one line per sense), an inflection line has
/// an empty gloss and names its lemma and form label (e.g. GLEYED, v, empty, GLEY, "past tense of").
/// Lines starting with '#' and malformed lines are ignored.
/// </summary>
public sealed class DefinitionBook
{
    private readonly Dictionary<string, List<WordSense>> _senses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Pos, string Lemma, string Form)> _inflections = new(StringComparer.Ordinal);

    public int Count => _senses.Count + _inflections.Count;

    public static DefinitionBook Read(TextReader reader)
    {
        var book = new DefinitionBook();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] fields = line.Split('\t');
            if (fields.Length != 5 || fields[0].Length == 0 || fields[1].Length == 0)
                continue;

            string word = fields[0].ToUpperInvariant();
            if (fields[2].Length > 0)
            {
                if (!book._senses.TryGetValue(word, out var senses))
                    book._senses[word] = senses = new();
                senses.Add(new WordSense(fields[1], fields[2]));
            }
            else if (fields[3].Length > 0)
            {
                book._inflections[word] = (fields[1], fields[3].ToUpperInvariant(), fields[4]);
            }
        }
        return book;
    }

    /// <summary>The definition of <paramref name="word"/> (any case), or null if none is on file.</summary>
    public WordDefinition? Define(string word)
    {
        string key = word.Trim().ToUpperInvariant();
        if (_senses.TryGetValue(key, out var senses))
            return new WordDefinition(key, senses.ToImmutableArray());

        if (_inflections.TryGetValue(key, out var inflection) && _senses.TryGetValue(inflection.Lemma, out var lemmaSenses))
        {
            var matching = lemmaSenses.Where(s => s.PartOfSpeech == inflection.Pos).ToImmutableArray();
            return new WordDefinition(key, matching.IsEmpty ? lemmaSenses.ToImmutableArray() : matching, inflection.Lemma, inflection.Form);
        }
        return null;
    }
}

/// <summary>Loads the bundled definitions (Open English WordNet, CC BY 4.0) embedded in this assembly.</summary>
public static class DefinitionLoader
{
    private const string Resource = "Crossword.Core.Lexicon.definitions.tsv.gz";

    private static readonly Lazy<DefinitionBook> Cached = new(Load);

    /// <summary>Shared, lazily loaded definitions. Read-only after loading, safe to share across threads.</summary>
    public static DefinitionBook Default => Cached.Value;

    private static DefinitionBook Load()
    {
        using var stream = typeof(DefinitionLoader).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded resource '{Resource}' not found.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return DefinitionBook.Read(reader);
    }
}
