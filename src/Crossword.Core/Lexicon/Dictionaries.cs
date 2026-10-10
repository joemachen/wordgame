using System.Collections.Concurrent;
using System.Collections.Immutable;
using Crossword.Core.Scoring;

namespace Crossword.Core.Lexicon;

/// <summary>
/// A dictionary overlay: extra words legal on top of ENABLE in a run that turns it on. <see cref="Kind"/> says what the
/// words are ("Acronyms &amp; Initialisms"); entries shorter than <see cref="MinLength"/> are left out. Its definitions
/// show as "<see cref="SenseLabel"/>. &lt;the entry's text&gt;" ("abbr. central processing unit").
/// A <b>theme</b> dictionary (<see cref="ThemeMult"/> &gt; 0) also gives that much Mult for each of its words a play forms,
/// ENABLE words included (<see cref="Dictionaries.Theme"/>); a plain overlay only adds words ENABLE doesn't have.
/// </summary>
public sealed record DictionaryDefinition(string Id, string Name, string Kind, string Description, int MinLength, string SenseLabel,
    decimal ThemeMult = 0)
{
    public bool IsTheme => ThemeMult > 0;
}

/// <summary>
/// Dictionary overlays (ROADMAP §1), unlocked one per run won in the order of <see cref="All"/> once The Lexicographer's
/// Deck is (<see cref="Profile.StatsQueries.UnlockedDictionaries"/>). A run's dictionaries live in
/// <see cref="Domain.RunState.Dictionaries"/>; their words are merged with ENABLE into the run's word graph
/// (<see cref="LexiconLoader.For"/>). Data: embedded <c>Lexicon/Data/&lt;id&gt;.tsv</c>, one <c>WORD&lt;TAB&gt;expansion</c>
/// (or description) per line, '#' comments and blank lines ignored. Every entry goes through <see cref="Denylist.Default"/>.
/// </summary>
public static class Dictionaries
{
    public const string TechShorthandId = "tech-shorthand";
    public const string AtlasId = "atlas";
    public const string OldeFolioId = "olde-folio";
    public const decimal OldeFolioMult = 3;

    public static ImmutableArray<DictionaryDefinition> All { get; } =
    [
        new(TechShorthandId, "The Tech Shorthand", "Acronyms & Initialisms",
            "Acronyms and initialisms of 3+ letters are legal: CPU, NASA, FAQ…", 3, "abbr"),
        new(AtlasId, "The Atlas Unlocked", "Proper Nouns & Places",
            "Place names of 3+ letters are legal: OSLO, ERIE, PERU…", 3, "n"),
        new(OldeFolioId, "The Olde English Folio", "Archaic English",
            $"Each archaic word a play forms adds +{OldeFolioMult} Mult: THEE, HATH, ERE, YON… (and O'ER, NE'ER, E'ER are legal as OER, NEER, EER)",
            3, "arch", ThemeMult: OldeFolioMult),
    ];

    private static readonly ConcurrentDictionary<string, Lazy<ImmutableDictionary<string, string>>> Cache = new();

    public static DictionaryDefinition? Find(string? id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DictionaryDefinition Get(string id) =>
        Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such dictionary.");

    /// <summary>
    /// The words of dictionary <paramref name="id"/> (uppercase), denied words and short entries removed. A theme
    /// dictionary's include ENABLE words.
    /// </summary>
    public static IEnumerable<string> Words(string id) => Entries(id).Keys;

    /// <summary>The bonus of the first theme dictionary among <paramref name="ids"/>, or null (a run holds one dictionary).</summary>
    public static WordTheme? Theme(IEnumerable<string> ids) =>
        ids.Select(Get).FirstOrDefault(d => d.IsTheme) is { } theme
            ? new WordTheme(theme.Name, Entries(theme.Id).Keys.ToImmutableHashSet(StringComparer.Ordinal), theme.ThemeMult)
            : null;

    /// <summary>
    /// The definition of <paramref name="word"/> from the first of <paramref name="ids"/> that has it (the dictionary's
    /// <see cref="DictionaryDefinition.SenseLabel"/> + the entry's text), or null.
    /// </summary>
    public static WordDefinition? Define(string word, IEnumerable<string> ids)
    {
        string key = word.Trim().ToUpperInvariant();
        foreach (string id in ids)
            if (Entries(id).TryGetValue(key, out var expansion))
                return new WordDefinition(key, [new WordSense(Get(id).SenseLabel, expansion)]);
        return null;
    }

    /// <summary>
    /// Parses dictionary data (see the class summary): entries that aren't words of <paramref name="minLength"/>+
    /// letters, are denied, or repeat an earlier word are skipped. Public for tests.
    /// </summary>
    public static ImmutableDictionary<string, string> Read(TextReader reader, int minLength, Denylist denylist)
    {
        var entries = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0 || line.TrimStart()[0] == '#')
                continue;
            int tab = line.IndexOf('\t');
            string text = tab < 0 ? line : line[..tab];
            string expansion = tab < 0 ? "" : line[(tab + 1)..].Trim();
            if (WordNormalizer.TryNormalize(text, out var word) && word.Length >= minLength && !denylist.Contains(word))
                entries.TryAdd(word, expansion);
        }
        return entries.ToImmutable();
    }

    private static ImmutableDictionary<string, string> Entries(string id)
    {
        var definition = Get(id);
        return Cache.GetOrAdd(definition.Id, _ => new Lazy<ImmutableDictionary<string, string>>(() => Load(definition))).Value;
    }

    private static ImmutableDictionary<string, string> Load(DictionaryDefinition definition)
    {
        string resource = $"Crossword.Core.Lexicon.{definition.Id}.tsv";
        using var stream = typeof(Dictionaries).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' not found.");
        using var reader = new StreamReader(stream);
        return Read(reader, definition.MinLength, Denylist.Default);
    }
}
