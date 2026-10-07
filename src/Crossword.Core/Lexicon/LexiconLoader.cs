namespace Crossword.Core.Lexicon;

/// <summary>Loads the bundled ENABLE word list (public domain) embedded in this assembly.</summary>
public static class LexiconLoader
{
    private const string EnableResource = "Crossword.Core.Lexicon.enable1.txt";

    private static readonly Lazy<Dawg> CachedEnable = new(() => Dawg.Build(ReadEnableWords()));

    /// <summary>Shared, lazily built ENABLE DAWG (denied words removed). Immutable, safe to share across runs.</summary>
    public static IWordGraph Enable => CachedEnable.Value;

    /// <summary>The ENABLE words the game uses: the raw list without <see cref="Denylist.Default"/>'s words.</summary>
    public static IEnumerable<string> ReadEnableWords() => Denylist.Default.Filter(ReadUnfilteredEnableWords());

    /// <summary>The raw ENABLE list, denied words included. For tests and data tools only — never build a lexicon from it.</summary>
    public static IEnumerable<string> ReadUnfilteredEnableWords()
    {
        using var stream = typeof(LexiconLoader).Assembly.GetManifestResourceStream(EnableResource)
            ?? throw new InvalidOperationException($"Embedded resource '{EnableResource}' not found.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
