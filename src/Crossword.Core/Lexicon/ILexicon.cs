namespace Crossword.Core.Lexicon;

/// <summary>
/// Read-only word list. Lookups are case-insensitive; implementations are immutable and thread-safe.
/// </summary>
public interface ILexicon
{
    int WordCount { get; }

    bool Contains(string word);

    /// <summary>True if any word in the lexicon starts with <paramref name="prefix"/> (including the word itself).</summary>
    bool HasPrefix(string prefix);
}
