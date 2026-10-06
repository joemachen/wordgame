using System.Collections.Immutable;
using Crossword.Core.Domain;

namespace Crossword.Core.Scoring;

/// <summary>
/// Base stats for a play whose longest word has at least <see cref="MinLength"/> letters.
/// Each Style Guide upgrade adds <see cref="LevelChips"/> and <see cref="LevelMult"/>.
/// </summary>
public sealed record WordTier(int MinLength, long BaseChips, decimal BaseMult, long LevelChips = 0, decimal LevelMult = 0)
{
    public string Label(bool isTopTier) => isTopTier ? $"{MinLength}+-letter" : $"{MinLength}-letter";
}

/// <summary>
/// All scoring numbers in one place so balance can be tuned without code changes.
/// <see cref="Default"/> was tuned with greedy simulations (CLI 'sim'): flat-ish tier Mult (1→3) plus +3 Mult per
/// intersection makes building onto the grid outscore isolated long words (~77% of non-opening greedy plays
/// intersect). Still provisional pending human playtests.
/// <see cref="BonusMult"/> is a flat per-play Mult added before Desk Items; it is only set for a round in progress
/// (Red Ink Bottle, via <see cref="Domain.RoundConfig.EffectiveScoring"/>).
/// <see cref="RepeatWordsScoreZero"/> (boss Redundant Copy): a word already formed earlier this round adds no letter chips.
/// </summary>
public sealed record ScoringConfig(
    ImmutableArray<WordTier> Tiers,
    ImmutableDictionary<char, int> LetterValues,
    decimal IntersectionMult,
    long BoldChips = 10,
    decimal ItalicMult = 2,
    int GildedMoney = 1,
    decimal BonusMult = 0,
    bool RepeatWordsScoreZero = false)
{
    public static ScoringConfig Default { get; } = new(
        Tiers:
        [
            new WordTier(2, 2, 1, LevelChips: 3, LevelMult: 1),
            new WordTier(3, 5, 1, LevelChips: 5, LevelMult: 1),
            new WordTier(4, 10, 2, LevelChips: 10, LevelMult: 1),
            new WordTier(5, 20, 2, LevelChips: 15, LevelMult: 1),
            new WordTier(6, 30, 3, LevelChips: 20, LevelMult: 1),
            new WordTier(7, 40, 3, LevelChips: 25, LevelMult: 1),
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

    /// <summary>A tile's letter chips: wild tiles are worth 0 whatever letter they play as.</summary>
    public int ValueOf(Tile tile) => tile.IsWild ? 0 : ValueOf(tile.Letter);

    /// <summary>Applies Style Guide upgrades (keyed by tier MinLength → number of upgrades).</summary>
    public ScoringConfig WithUpgrades(IReadOnlyDictionary<int, int> upgrades) =>
        upgrades.Count == 0
            ? this
            : this with
            {
                Tiers = Tiers.Select(t => upgrades.TryGetValue(t.MinLength, out int n) && n > 0
                        ? t with { BaseChips = t.BaseChips + n * t.LevelChips, BaseMult = t.BaseMult + n * t.LevelMult }
                        : t)
                    .ToImmutableArray(),
            };
}
