using System.IO.Compression;
using System.Text;
using System.Xml;
using Crossword.Core.Lexicon;

namespace Crossword.DefinitionsBuilder;

/// <summary>
/// Builds <c>src/Crossword.Core/Lexicon/Data/definitions.tsv.gz</c> from Open English WordNet (CC BY 4.0) for the
/// ENABLE words. Lemmas get their most common sense (up to two parts of speech); inflected forms ("GLEYED") point
/// at their lemma via WordNet's irregular forms and "morphy"-style suffix rules. Line format: see
/// <see cref="DefinitionBook"/>.
///
/// Usage: dotnet run -c Release --project tools/Crossword.DefinitionsBuilder [wordnet.xml.gz] [out.tsv.gz]
/// </summary>
public static class Program
{
    private const int MaxSensesPerWord = 2;
    private const int MaxGlossLength = 110;

    // (lemma, part of speech) candidates come from these suffix swaps, tried in order (WordNet morphy rules plus
    // doubled consonants, e.g. ABETTED → ABET, BIGGER → BIG).
    private static readonly (string Pos, string Suffix, string Replacement)[] Rules =
    [
        ("n", "ses", "s"), ("n", "xes", "x"), ("n", "zes", "z"), ("n", "ches", "ch"), ("n", "shes", "sh"),
        ("n", "men", "man"), ("n", "ies", "y"), ("n", "s", ""),
        ("v", "ies", "y"), ("v", "es", "e"), ("v", "es", ""), ("v", "s", ""),
        ("v", "ed", "e"), ("v", "ed", ""), ("v", "ing", "e"), ("v", "ing", ""),
        ("a", "iest", "y"), ("a", "ier", "y"), ("a", "est", "e"), ("a", "est", ""), ("a", "er", "e"), ("a", "er", ""),
    ];

    public static int Main(string[] args)
    {
        string root = FindRepoRoot();
        bool candidates = args.Contains("--denylist-candidates");
        args = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        string input = args.Length > 0 ? args[0] : Path.Combine(root, "tools", "data", "english-wordnet-2025.xml.gz");
        string output = args.Length > 1 ? args[1] : candidates
            ? Path.Combine(root, "tools", "data", "denylist-candidates.txt")
            : Path.Combine(root, "src", "Crossword.Core", "Lexicon", "Data", "definitions.tsv.gz");
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"WordNet file not found: {input}");
            return 1;
        }

        var wordNet = WordNet.Load(input);
        Console.WriteLine($"WordNet: {wordNet.Senses.Count:N0} single-word lemmas, {wordNet.Forms.Count:N0} irregular forms, " +
                          $"{wordNet.Offensive.Values.Sum(s => s.Count):N0} senses tagged offensive (slurs and crude glosses hidden)");
        if (candidates)
            return WriteDenylistCandidates(wordNet, output);

        var denylist = Denylist.Default;
        var enable = LexiconLoader.ReadEnableWords().Select(w => w.Trim().ToUpperInvariant()).Where(w => w.Length > 0).ToList();
        var enableSet = enable.ToHashSet();
        var supplement = ReadSupplement(Path.Combine(root, "tools", "Crossword.DefinitionsBuilder", "supplement.txt"));
        foreach (string word in supplement.Keys.Where(w => !enableSet.Contains(w)))
            Console.WriteLine($"  warning: supplement word {word} is not in ENABLE (or is denied)");
        foreach (var (word, senses) in supplement)
            foreach (var (_, gloss) in senses.Where(s => MentionsDenied(s.Gloss, denylist)))
                Console.WriteLine($"  warning: supplement gloss for {word} mentions a denied word: {gloss}");

        // Glosses that use a denied word are never shown; neither are lemmas that are themselves denied.
        foreach (var senses in wordNet.Senses.Values)
            senses.RemoveAll(s => MentionsDenied(s.Gloss, denylist));
        foreach (string denied in denylist.Words)
            wordNet.Senses.Remove(denied);

        var lines = new List<string>();
        var defined = new HashSet<string>();
        var lemmasNeeded = new HashSet<string>();
        int direct = 0, inflected = 0;
        var missing = new List<string>();

        foreach (string word in enable)
        {
            if (supplement.TryGetValue(word, out var own))
            {
                foreach (var (pos, gloss) in own)
                    lines.Add($"{word}\t{pos}\t{gloss}\t\t");
                defined.Add(word);
            }
            else if (wordNet.Senses.TryGetValue(word, out var senses) && senses.Count > 0 && !PreferInflection(word, senses, wordNet))
            {
                foreach (var (pos, gloss) in PickSenses(senses))
                    lines.Add($"{word}\t{pos}\t{Trim(gloss)}\t\t");
                defined.Add(word);
                direct++;
            }
            else if (FindLemma(word, wordNet) is { } lemma)
            {
                lines.Add($"{word}\t{lemma.Pos}\t\t{lemma.Lemma}\t{FormLabel(word, lemma.Pos)}");
                lemmasNeeded.Add(lemma.Lemma);
                inflected++;
            }
            else
            {
                missing.Add(word);
            }
        }

        // Inflections can point at lemmas ENABLE doesn't list; include those lemmas' senses so every redirect resolves.
        foreach (string lemma in lemmasNeeded.Where(l => !defined.Contains(l)).Order())
            foreach (var (pos, gloss) in PickSenses(wordNet.Senses[lemma]))
                lines.Add($"{lemma}\t{pos}\t{Trim(gloss)}\t\t");

        using (var file = File.Create(output))
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            writer.WriteLine("# Definitions from Open English WordNet 2025 (CC BY 4.0, https://en-word.net). Generated by tools/Crossword.DefinitionsBuilder.");
            foreach (string line in lines)
                writer.WriteLine(line);
        }

        double total = enable.Count;
        Console.WriteLine($"ENABLE words: {enable.Count:N0}");
        Console.WriteLine($"  hand-written:      {supplement.Keys.Count(enableSet.Contains),7:N0}");
        Console.WriteLine($"  defined directly:  {direct,7:N0} ({direct / total:P1})");
        Console.WriteLine($"  via inflection:    {inflected,7:N0} ({inflected / total:P1})");
        Console.WriteLine($"  no definition:     {missing.Count,7:N0} ({missing.Count / total:P1})");
        var shortMissing = missing.Where(w => w.Length <= 5).ToList();
        Console.WriteLine($"  missing among 2-5 letter words: {shortMissing.Count:N0} of {enable.Count(w => w.Length <= 5):N0}");
        Console.WriteLine($"  sample missing: {string.Join(", ", shortMissing.Where((_, i) => i % Math.Max(1, shortMissing.Count / 40) == 0).Take(40))}");
        Console.WriteLine($"Wrote {output} ({new FileInfo(output).Length / 1024:N0} KB)");
        return 0;
    }

    /// <summary>
    /// True if <paramref name="gloss"/> contains a denied word as a whole lowercase word. Capitalised words are names
    /// ("Homo sapiens").
    /// </summary>
    private static bool MentionsDenied(string gloss, Denylist denylist) =>
        gloss.Split(GlossSeparators, StringSplitOptions.RemoveEmptyEntries).Any(w => char.IsLower(w[0]) && denylist.Contains(w));

    private static readonly char[] GlossSeparators = [.. " \t,;:.!?()[]\"'/-–—…".ToCharArray()];

    /// <summary>
    /// Lists every unfiltered ENABLE word with a sense WordNet tags as an ethnic slur, slur, disparagement or
    /// obscenity, plus its gloss and whether the denylist already has it — raw material for a hand-reviewed
    /// <c>denylist.txt</c>. WordNet tags far more than slurs (generic insults, profanity), so never copy it wholesale.
    /// </summary>
    private static int WriteDenylistCandidates(WordNet wordNet, string output)
    {
        var enable = LexiconLoader.ReadUnfilteredEnableWords().Select(w => w.Trim().ToUpperInvariant()).ToHashSet();
        var rows = wordNet.Offensive
            .Where(kv => enable.Contains(kv.Key))
            .SelectMany(kv => kv.Value.Select(s => (Word: kv.Key, s.Domain, s.Pos, s.Gloss)))
            .OrderBy(r => r.Domain, StringComparer.Ordinal).ThenBy(r => r.Word, StringComparer.Ordinal)
            .Select(r => $"{r.Domain}\t{r.Word}\t{(Denylist.Default.Contains(r.Word) ? "denied" : "")}\t{r.Pos}\t{r.Gloss}")
            .ToList();
        File.WriteAllLines(output, rows);
        Console.WriteLine($"Wrote {rows.Count:N0} tagged senses of ENABLE words to {output}");
        return 0;
    }

    /// <summary>
    /// "-ED" words that WordNet only knows as adjectives read better as verb forms (HOGGED → past tense of HOG,
    /// not "(of a ship) so weakened as to sag at each end").
    /// </summary>
    private static bool PreferInflection(string word, List<(string Pos, string Gloss)> senses, WordNet wordNet) =>
        word.EndsWith("ED", StringComparison.Ordinal) && senses.All(s => s.Pos == "a") && FindLemma(word, wordNet) is { Pos: "v" };

    /// <summary>Hand-written senses (supplement.txt): "WORD | pos | gloss" lines, '#' comments, file order kept.</summary>
    private static Dictionary<string, List<(string Pos, string Gloss)>> ReadSupplement(string path)
    {
        var result = new Dictionary<string, List<(string Pos, string Gloss)>>();
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('|', StringSplitOptions.TrimEntries);
            if (line.StartsWith('#') || fields.Length != 3)
                continue;
            string word = fields[0].ToUpperInvariant();
            if (!result.TryGetValue(word, out var senses))
                result[word] = senses = new();
            senses.Add((fields[1], fields[2]));
        }
        return result;
    }

    /// <summary>The first sense of the most-used parts of speech (most senses first), at most two.</summary>
    private static IEnumerable<(string Pos, string Gloss)> PickSenses(List<(string Pos, string Gloss)> senses) =>
        senses.GroupBy(s => s.Pos)
            .OrderByDescending(g => g.Count())
            .Take(MaxSensesPerWord)
            .Select(g => g.First());

    private static (string Lemma, string Pos)? FindLemma(string word, WordNet wordNet)
    {
        if (wordNet.Forms.TryGetValue(word, out var irregular) && wordNet.HasPos(irregular.Lemma, irregular.Pos))
            return irregular;

        var candidates = new List<(string Lemma, string Pos)>();
        foreach (var (pos, suffix, replacement) in Rules)
        {
            if (!word.EndsWith(suffix.ToUpperInvariant(), StringComparison.Ordinal) || word.Length - suffix.Length < 2)
                continue;
            string stem = word[..^suffix.Length];
            foreach (string lemma in new[] { stem + replacement.ToUpperInvariant(), Undouble(stem, replacement) })
            {
                if (lemma.Length >= 2 && wordNet.HasPos(lemma, pos))
                    candidates.Add((lemma, pos));
            }
        }
        if (candidates.Count == 0)
            return null;
        // Prefer the reading that matches the lemma's main part of speech ("RUNS" → verb, "HOUSES" → noun).
        return candidates.FirstOrDefault(c => wordNet.MainPos(c.Lemma) == c.Pos) is { Lemma: not null } main ? main : candidates[0];
    }

    /// <summary>ABETT → ABET for "-ed"/"-ing"/"-er"/"-est" forms that doubled the final consonant.</summary>
    private static string Undouble(string stem, string replacement) =>
        replacement.Length == 0 && stem.Length >= 3 && stem[^1] == stem[^2] && !"AEIOU".Contains(stem[^1]) ? stem[..^1] : "";

    private static string FormLabel(string word, string pos) => pos switch
    {
        "n" => "plural of",
        "v" when word.EndsWith("ING", StringComparison.Ordinal) => "present participle of",
        "v" when word.EndsWith('S') => "third-person singular of",
        "v" => "past tense of",
        "a" when word.EndsWith("EST", StringComparison.Ordinal) => "superlative of",
        "a" when word.EndsWith("ER", StringComparison.Ordinal) => "comparative of",
        _ => "form of",
    };

    /// <summary>Shortens a gloss at a "; " boundary, else at a word boundary with an ellipsis.</summary>
    private static string Trim(string gloss)
    {
        gloss = gloss.Replace('\t', ' ').Replace('\n', ' ').Trim();
        if (gloss.Length <= MaxGlossLength)
            return gloss;
        int cut = gloss.LastIndexOf("; ", MaxGlossLength, StringComparison.Ordinal);
        if (cut >= 30)
            return gloss[..cut];
        cut = gloss.LastIndexOf(' ', MaxGlossLength - 1);
        return gloss[..(cut > 30 ? cut : MaxGlossLength - 1)].TrimEnd(',', ';', ' ') + "…";
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "wordgame.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}

/// <summary>The parts of a WN-LMF file we need: single-word lemmas with ordered senses, and irregular forms.</summary>
internal sealed class WordNet
{
    /// <summary>
    /// WORD → senses in WordNet order (most common first within each entry). POS: n, v, a, r. Slur senses and crude
    /// glosses are left out (see <see cref="IsHidden"/>).
    /// </summary>
    public Dictionary<string, List<(string Pos, string Gloss)>> Senses { get; } = new();

    /// <summary>WORD → every sense WordNet tags with an offensive usage domain (shown or not), with that domain.</summary>
    public Dictionary<string, List<(string Pos, string Gloss, string Domain)>> Offensive { get; } = new();

    /// <summary>Irregular inflected form → (lemma, POS), e.g. ABETTED → (ABET, v).</summary>
    public Dictionary<string, (string Lemma, string Pos)> Forms { get; } = new();

    public bool HasPos(string word, string pos) => Senses.TryGetValue(word, out var senses) && senses.Any(s => s.Pos == pos);

    public string? MainPos(string word) =>
        Senses.TryGetValue(word, out var senses) ? senses.GroupBy(s => s.Pos).OrderByDescending(g => g.Count()).First().Key : null;

    public static WordNet Load(string path)
    {
        var entries = new List<(string Word, string Pos, List<(string Synset, string Sense)> Senses, List<string> Forms)>();
        var definitions = new Dictionary<string, string>();
        var senseSynset = new Dictionary<string, string>();       // sense id → synset id
        var usages = new List<(string Source, string Target)>();   // "exemplifies": sense/synset → usage-domain sense/synset
        var domainSynsets = new Dictionary<string, string>();      // offensive usage-domain synset → label

        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = XmlReader.Create(gzip, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        (string Word, string Pos, List<(string Synset, string Sense)> Senses, List<string> Forms)? entry = null;
        string? sense = null, synset = null;
        bool definitionPending = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "LexicalEntry" && entry is { } done)
            {
                entries.Add(done);
                entry = null;
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.Name)
            {
                case "LexicalEntry":
                    entry = ("", "", new List<(string, string)>(), new List<string>());
                    break;
                case "Lemma" when entry is { } e:
                    entry = e with { Word = reader.GetAttribute("writtenForm") ?? "", Pos = NormalizePos(reader.GetAttribute("partOfSpeech")) };
                    break;
                case "Form" when entry is { } e:
                    e.Forms.Add(reader.GetAttribute("writtenForm") ?? "");
                    break;
                case "Sense" when entry is { } e:
                    sense = reader.GetAttribute("id") ?? "";
                    string senseSynsetId = reader.GetAttribute("synset") ?? "";
                    senseSynset[sense] = senseSynsetId;
                    e.Senses.Add((senseSynsetId, sense));
                    break;
                case "SenseRelation" when sense is not null && reader.GetAttribute("relType") == "exemplifies":
                    usages.Add((sense, reader.GetAttribute("target") ?? ""));
                    break;
                case "Synset":
                    synset = reader.GetAttribute("id");
                    definitionPending = true;
                    string members = " " + reader.GetAttribute("members") + " ";
                    if (synset is not null && OffensiveDomains.FirstOrDefault(d => members.Contains($" oewn-{d.Lemma}-n ")).Label is { } label)
                        domainSynsets[synset] = label;
                    break;
                case "Definition" when synset is not null && definitionPending:
                    definitions.TryAdd(synset, reader.ReadElementContentAsString());
                    definitionPending = false; // first definition only
                    break;
                case "SynsetRelation" when synset is not null && reader.GetAttribute("relType") == "exemplifies":
                    usages.Add((synset, reader.GetAttribute("target") ?? ""));
                    break;
            }
        }

        // A sense is offensive when it or its synset exemplifies an offensive usage domain. Domain synsets are found by
        // member lemma; only those something actually points at count (SLUR the musical mark is no usage domain).
        var offensiveSources = new Dictionary<string, string>();
        foreach (var (source, target) in usages)
            if (domainSynsets.TryGetValue(senseSynset.GetValueOrDefault(target, target), out var label))
                offensiveSources.TryAdd(source, label);

        var wordNet = new WordNet();
        // Lowercase entries first so a common noun's senses come before a capitalised homograph's (e.g. "yule"/"Yule"),
        // and plain words before hyphenated/spaced ones joined up ("fly-by" → FLYBY), which only fill gaps.
        var ordered = entries
            .Select(e => (Entry: e, Key: Normalize(e.Word), Joined: false))
            .Concat(entries.Select(e => (Entry: e, Key: Normalize(e.Word.Replace("-", "").Replace(" ", "")), Joined: true)))
            .Where(x => x.Key is not null && !(x.Joined && Normalize(x.Entry.Word) is not null) && !IsAbbreviation(x.Entry.Word))
            .OrderBy(x => x.Joined ? 1 : 0)
            .ThenBy(x => char.IsUpper(x.Entry.Word.FirstOrDefault()) ? 1 : 0)
            .ToList();
        var plainKeys = ordered.Where(x => !x.Joined).Select(x => x.Key!).ToHashSet();
        foreach (var ((word, pos, senses, forms), key, joined) in ordered.Select(x => (x.Entry, x.Key!, x.Joined)))
        {
            if (joined && plainKeys.Contains(key))
                continue;
            if (!wordNet.Senses.TryGetValue(key, out var clean))
                wordNet.Senses[key] = clean = new();
            foreach (var (synsetId, senseId) in senses)
            {
                if (!definitions.TryGetValue(synsetId, out var gloss))
                    continue;
                string? label = offensiveSources.GetValueOrDefault(senseId) ?? offensiveSources.GetValueOrDefault(synsetId);
                if (label is not null)
                {
                    if (!wordNet.Offensive.TryGetValue(key, out var tagged))
                        wordNet.Offensive[key] = tagged = new();
                    tagged.Add((pos, gloss, label));
                }
                if (!IsHidden(gloss, label))
                    clean.Add((pos, gloss));
            }
            foreach (string form in forms)
                if (Normalize(form) is { } formKey)
                    wordNet.Forms.TryAdd(formKey, (key, pos));
        }
        return wordNet;
    }

    /// <summary>
    /// Whether a sense is never shown. WordNet tags whole synsets with a usage domain, so a tag alone is too noisy
    /// ("an adult female person" is tagged disparagement, "the fleshy part … you sit on" obscenity): a tagged sense is
    /// hidden when it is an ethnic slur or its gloss is itself crude; an untagged one when its gloss names an offensive term.
    /// </summary>
    private static bool IsHidden(string gloss, string? label) =>
        label is "ethnic slur"
        || (label is not null && CrudeGlossMarkers.Any(m => gloss.Contains(m, StringComparison.OrdinalIgnoreCase)))
        || OffensiveTermMarkers.Any(m => gloss.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] OffensiveTermMarkers =
        ["ethnic slur", "offensive term", "offensive name", "obscene term", "obscene word", "vulgar slang", "derogatory term"];

    private static readonly string[] CrudeGlossMarkers =
        ["slur", "offensive term", "offensive name", "offensive)", "obscene", "vulgar slang", "vulgar term", "derogatory",
         "disparaging", "have sexual intercourse with", "slang for sexual intercourse", "fellatio"];

    /// <summary>Usage domains that count as offensive, by a member lemma of the domain's synset.</summary>
    private static readonly (string Lemma, string Label)[] OffensiveDomains =
    [
        ("ethnic_slur", "ethnic slur"), ("slur", "slur"), ("disparagement", "disparagement"), ("derogation", "disparagement"),
        ("obscenity", "obscenity"), ("vulgarism", "obscenity"),
    ];

    /// <summary>Acronyms (WHO, ER) and element symbols (As, Na) aren't the words players spell.</summary>
    private static bool IsAbbreviation(string word) =>
        word.Count(char.IsUpper) >= 2 || (word.Length <= 2 && char.IsUpper(word.FirstOrDefault()));

    /// <summary>Satellite adjectives (s) count as adjectives.</summary>
    private static string NormalizePos(string? pos) => pos == "s" ? "a" : pos ?? "";

    /// <summary>Uppercased single words of letters only; null for phrases, hyphenations, digits.</summary>
    private static string? Normalize(string word)
    {
        string upper = word.ToUpperInvariant();
        return upper.Length >= 2 && upper.All(c => c is >= 'A' and <= 'Z') ? upper : null;
    }
}
