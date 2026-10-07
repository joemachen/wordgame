using System.Collections.Immutable;

namespace Crossword.Core.Lexicon;

/// <summary>
/// Words the game never accepts, whatever word list they come from: slurs (words that target a group). Profanity is
/// not on it. Every word list (ENABLE, future dictionary overlays) goes through <see cref="Filter"/> before its word
/// graph is built, so a denied word is never legal, suggested or defined. Data lines: one word each (any case), every
/// inflection listed explicitly; '#' comments and blank lines are ignored.
/// </summary>
public sealed class Denylist
{
    private const string Resource = "Crossword.Core.Lexicon.denylist.txt";

    private static readonly Lazy<Denylist> Cached = new(Load);

    private Denylist(ImmutableHashSet<string> words) => Words = words;

    /// <summary>The bundled denylist. Immutable, safe to share across threads.</summary>
    public static Denylist Default => Cached.Value;

    /// <summary>Denied words, uppercase.</summary>
    public ImmutableHashSet<string> Words { get; }

    public static Denylist Read(TextReader reader)
    {
        var words = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            string text = line.Trim();
            if (text.Length == 0 || text[0] == '#')
                continue;
            if (!WordNormalizer.TryNormalize(text, out var word))
                throw new FormatException($"Denylist entry '{text}' is not a word of {WordNormalizer.MinLength}–{WordNormalizer.MaxLength} letters.");
            words.Add(word);
        }
        return new Denylist(words.ToImmutable());
    }

    /// <summary>True if <paramref name="word"/> (any case, surrounding spaces ignored) is denied.</summary>
    public bool Contains(string word) => Words.Contains(word.Trim().ToUpperInvariant());

    /// <summary><paramref name="words"/> without the denied ones, in their original order and spelling.</summary>
    public IEnumerable<string> Filter(IEnumerable<string> words) => words.Where(w => !Contains(w));

    private static Denylist Load()
    {
        using var stream = typeof(Denylist).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded resource '{Resource}' not found.");
        using var reader = new StreamReader(stream);
        return Read(reader);
    }
}
