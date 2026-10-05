namespace Crossword.Core.Lexicon;

/// <summary>Loads the bundled ENABLE word list (public domain) embedded in this assembly.</summary>
public static class LexiconLoader
{
    private const string EnableResource = "Crossword.Core.Lexicon.enable1.txt";

    private static readonly Lazy<Dawg> CachedEnable = new(() => Dawg.Build(ReadEnableWords()));

    /// <summary>Shared, lazily built ENABLE DAWG. Immutable, safe to share across runs.</summary>
    public static ILexicon Enable => CachedEnable.Value;

    public static IEnumerable<string> ReadEnableWords()
    {
        using var stream = typeof(LexiconLoader).Assembly.GetManifestResourceStream(EnableResource)
            ?? throw new InvalidOperationException($"Embedded resource '{EnableResource}' not found.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
