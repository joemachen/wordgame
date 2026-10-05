using System.Text;

namespace Crossword.Core.Lexicon;

/// <summary>
/// Directed Acyclic Word Graph: a minimal automaton accepting exactly the lexicon's words.
/// Built with Daciuk et al.'s incremental algorithm for sorted input, then frozen into flat arrays.
/// Supports prefix traversal, which future move generation / dead-board detection relies on.
/// </summary>
public sealed class Dawg : ILexicon
{
    private const int Root = 0;

    // Node i's outgoing edges are _edgeLabel/_edgeTarget[_firstEdge[i] .. _firstEdge[i + 1]), sorted by label.
    private readonly int[] _firstEdge;
    private readonly char[] _edgeLabel;
    private readonly int[] _edgeTarget;
    private readonly bool[] _terminal;

    public int WordCount { get; }

    public int NodeCount => _terminal.Length;

    private Dawg(int[] firstEdge, char[] edgeLabel, int[] edgeTarget, bool[] terminal, int wordCount)
    {
        _firstEdge = firstEdge;
        _edgeLabel = edgeLabel;
        _edgeTarget = edgeTarget;
        _terminal = terminal;
        WordCount = wordCount;
    }

    public bool Contains(string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;
        int node = Walk(word);
        return node >= 0 && _terminal[node];
    }

    public bool HasPrefix(string prefix) =>
        string.IsNullOrEmpty(prefix) ? WordCount > 0 : Walk(prefix) >= 0;

    /// <summary>Follows <paramref name="text"/> from the root; returns the reached node or -1.</summary>
    private int Walk(string text)
    {
        int node = Root;
        foreach (char raw in text)
        {
            node = Child(node, char.ToUpperInvariant(raw));
            if (node < 0)
                return -1;
        }
        return node;
    }

    private int Child(int node, char label)
    {
        for (int e = _firstEdge[node]; e < _firstEdge[node + 1]; e++)
        {
            if (_edgeLabel[e] == label)
                return _edgeTarget[e];
            if (_edgeLabel[e] > label)
                break;
        }
        return -1;
    }

    /// <summary>Builds a DAWG. Input need not be sorted or unique; invalid entries are skipped.</summary>
    public static Dawg Build(IEnumerable<string> words)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in words)
        {
            if (WordNormalizer.TryNormalize(raw, out var word))
                normalized.Add(word);
        }

        var builder = new Builder();
        foreach (var word in normalized)
            builder.Insert(word);
        return builder.Freeze(normalized.Count);
    }

    private sealed class BuildNode
    {
        public readonly SortedDictionary<char, BuildNode> Edges = new();
        public bool Terminal;
        public int RegisterId = -1;

        public string Signature()
        {
            var sb = new StringBuilder(Terminal ? "1" : "0");
            foreach (var (label, child) in Edges)
                sb.Append(label).Append(child.RegisterId).Append(',');
            return sb.ToString();
        }
    }

    private sealed class Builder
    {
        private readonly BuildNode _root = new();
        private readonly Dictionary<string, BuildNode> _register = new(StringComparer.Ordinal);
        private readonly List<(BuildNode Parent, char Label, BuildNode Child)> _unchecked = new();
        private string _previous = string.Empty;

        public void Insert(string word)
        {
            int common = 0;
            while (common < word.Length && common < _previous.Length && word[common] == _previous[common])
                common++;

            Minimize(common);

            var node = _unchecked.Count == 0 ? _root : _unchecked[^1].Child;
            for (int i = common; i < word.Length; i++)
            {
                var next = new BuildNode();
                node.Edges[word[i]] = next;
                _unchecked.Add((node, word[i], next));
                node = next;
            }
            node.Terminal = true;
            _previous = word;
        }

        /// <summary>Merges unchecked nodes deeper than <paramref name="downTo"/> with equivalent registered nodes.</summary>
        private void Minimize(int downTo)
        {
            for (int i = _unchecked.Count - 1; i >= downTo; i--)
            {
                var (parent, label, child) = _unchecked[i];
                string signature = child.Signature();
                if (_register.TryGetValue(signature, out var existing))
                {
                    parent.Edges[label] = existing;
                }
                else
                {
                    child.RegisterId = _register.Count;
                    _register[signature] = child;
                }
                _unchecked.RemoveAt(i);
            }
        }

        public Dawg Freeze(int wordCount)
        {
            Minimize(0);

            // Breadth-first numbering, root = 0.
            var index = new Dictionary<BuildNode, int>(ReferenceEqualityComparer.Instance) { [_root] = 0 };
            var order = new List<BuildNode> { _root };
            for (int i = 0; i < order.Count; i++)
            {
                foreach (var child in order[i].Edges.Values)
                {
                    if (index.TryAdd(child, order.Count))
                        order.Add(child);
                }
            }

            int edgeCount = order.Sum(n => n.Edges.Count);
            var firstEdge = new int[order.Count + 1];
            var labels = new char[edgeCount];
            var targets = new int[edgeCount];
            var terminal = new bool[order.Count];

            int e = 0;
            for (int n = 0; n < order.Count; n++)
            {
                firstEdge[n] = e;
                terminal[n] = order[n].Terminal;
                foreach (var (label, child) in order[n].Edges)
                {
                    labels[e] = label;
                    targets[e] = index[child];
                    e++;
                }
            }
            firstEdge[order.Count] = e;

            return new Dawg(firstEdge, labels, targets, terminal, wordCount);
        }
    }
}
