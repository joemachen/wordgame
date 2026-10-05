namespace Crossword.Core.Lexicon;

/// <summary>
/// Simple reference lexicon backed by a hash set and a sorted array (for prefix queries).
/// Used to cross-check <see cref="Dawg"/> and for small hand-built test lexicons.
/// </summary>
public sealed class HashSetLexicon : ILexicon
{
    private readonly HashSet<string> _words;
    private readonly string[] _sorted;

    public HashSetLexicon(IEnumerable<string> words)
    {
        _words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in words)
        {
            if (WordNormalizer.TryNormalize(raw, out var word))
                _words.Add(word);
        }
        _sorted = _words.Order(StringComparer.Ordinal).ToArray();
    }

    public int WordCount => _words.Count;

    public bool Contains(string word) =>
        !string.IsNullOrEmpty(word) && _words.Contains(word.ToUpperInvariant());

    public bool HasPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return _sorted.Length > 0;

        string upper = prefix.ToUpperInvariant();
        int index = Array.BinarySearch(_sorted, upper, StringComparer.Ordinal);
        if (index >= 0)
            return true;

        int insertAt = ~index;
        return insertAt < _sorted.Length && _sorted[insertAt].StartsWith(upper, StringComparison.Ordinal);
    }
}
