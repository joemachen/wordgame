using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Scoring;

/// <summary>Base stats for a play whose longest word has at least <see cref="MinLength"/> letters.</summary>
public sealed record WordTier(int MinLength, long BaseChips, decimal BaseMult);

/// <summary>
/// All scoring numbers in one place so balance can be tuned without code changes.
/// <see cref="Default"/> was tuned with greedy simulations (CLI 'sim'): flat-ish tier Mult (1→3) plus +3 Mult per
/// intersection makes building onto the grid outscore isolated long words (~77% of non-opening greedy plays
/// intersect). Still provisional pending human playtests.
/// </summary>
public sealed record ScoringConfig(
    ImmutableArray<WordTier> Tiers,
    ImmutableDictionary<char, int> LetterValues,
    decimal IntersectionMult,
    long BoldChips = 10,
    decimal ItalicMult = 2,
    int GildedMoney = 1)
{
    public static ScoringConfig Default { get; } = new(
        Tiers:
        [
            new WordTier(2, 2, 1),
            new WordTier(3, 5, 1),
            new WordTier(4, 10, 2),
            new WordTier(5, 20, 2),
            new WordTier(6, 30, 3),
            new WordTier(7, 40, 3),
        ],
        LetterValues: new Dictionary<char, int>
        {
            ['A'] = 1, ['B'] = 3, ['C'] = 3, ['D'] = 2, ['E'] = 1, ['F'] = 4, ['G'] = 2, ['H'] = 4, ['I'] = 1,
            ['J'] = 8, ['K'] = 5, ['L'] = 1, ['M'] = 3, ['N'] = 1, ['O'] = 1, ['P'] = 3, ['Q'] = 10, ['R'] = 1,
            ['S'] = 1, ['T'] = 1, ['U'] = 1, ['V'] = 4, ['W'] = 4, ['X'] = 8, ['Y'] = 4, ['Z'] = 10,
        }.ToImmutableDictionary(),
        IntersectionMult: 3);

    /// <summary>The highest tier whose minimum length is satisfied.</summary>
    public WordTier TierFor(int wordLength) =>
        Tiers.Where(t => t.MinLength <= wordLength).MaxBy(t => t.MinLength)
        ?? throw new ArgumentOutOfRangeException(nameof(wordLength), wordLength, "No tier covers this word length.");

    public int ValueOf(Letter letter) => LetterValues.GetValueOrDefault(letter.Char);
}
