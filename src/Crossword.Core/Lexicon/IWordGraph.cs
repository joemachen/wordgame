namespace Crossword.Core.Lexicon;

/// <summary>
/// Letter-by-letter traversal of a lexicon, used by move generation to prune dead prefixes cheaply.
/// Nodes are opaque ints; <see cref="NoNode"/> means the prefix leads nowhere.
/// </summary>
public interface IWordGraph : ILexicon
{
    const int NoNode = -1;

    int Root { get; }

    /// <summary>The node reached by appending <paramref name="letter"/> (uppercase A–Z), or <see cref="NoNode"/>.</summary>
    int Step(int node, char letter);

    /// <summary>True if the path to <paramref name="node"/> spells a complete word.</summary>
    bool IsTerminal(int node);
}
