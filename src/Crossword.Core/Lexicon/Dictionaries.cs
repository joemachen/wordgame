using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Crossword.Core.Lexicon;

/// <summary>
/// A dictionary overlay: extra words legal on top of ENABLE in a run that turns it on. <see cref="Kind"/> says what the
/// words are ("Acronyms &amp; Initialisms"); entries shorter than <see cref="MinLength"/> are left out.
/// </summary>
public sealed record DictionaryDefinition(string Id, string Name, string Kind, string Description, int MinLength);

/// <summary>
/// Dictionary overlays (ROADMAP §1), unlocked one per run won in the order of <see cref="All"/> once The Lexicographer's
/// Deck is (<see cref="Profile.StatsQueries.UnlockedDictionaries"/>). A run's dictionaries live in
/// <see cref="Domain.RunState.Dictionaries"/>; their words are merged with ENABLE into the run's word graph
/// (<see cref="LexiconLoader.For"/>). Data: embedded <c>Lexicon/Data/&lt;id&gt;.tsv</c>, one <c>WORD&lt;TAB&gt;expansion</c>
/// per line, '#' comments and blank lines ignored. Every entry goes through <see cref="Denylist.Default"/>.
/// </summary>
public static class Dictionaries
{
    public const string TechShorthandId = "tech-shorthand";

    public static ImmutableArray<DictionaryDefinition> All { get; } =
    [
        new(TechShorthandId, "The Tech Shorthand", "Acronyms & Initialisms",
            "Acronyms and initialisms of 3+ letters are legal: CPU, NASA, FAQ…", 3),
    ];

    private static readonly ConcurrentDictionary<string, Lazy<ImmutableDictionary<string, string>>> Cache = new();

    public static DictionaryDefinition? Find(string? id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public static DictionaryDefinition Get(string id) =>
        Find(id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such dictionary.");

    /// <summary>The words dictionary <paramref name="id"/> adds (uppercase), denied words and short entries removed.</summary>
    public static IEnumerable<string> Words(string id) => Entries(id).Keys;

    /// <summary>
    /// The definition of <paramref name="word"/> from the first of <paramref name="ids"/> that has it ("abbr." + its
    /// expansion), or null.
    /// </summary>
    public static WordDefinition? Define(string word, IEnumerable<string> ids)
    {
        string key = word.Trim().ToUpperInvariant();
        foreach (string id in ids)
            if (Entries(id).TryGetValue(key, out var expansion))
                return new WordDefinition(key, [new WordSense("abbr", expansion)]);
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
