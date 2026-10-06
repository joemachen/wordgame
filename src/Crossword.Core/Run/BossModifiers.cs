using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Random;
using Crossword.Core.Scoring;

namespace Crossword.Core.Run;

/// <summary>
/// A Sunday Edition rule twist. Modifiers are pure data transforms of the round and scoring config,
/// so every rule they change is enforced by the normal Core rules (validator, generator, scoring).
/// </summary>
public abstract record BossModifier
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    protected virtual RoundConfig ModifyRound(RoundConfig config) => config;

    public virtual ScoringConfig ModifyScoring(ScoringConfig scoring) => scoring;

    /// <summary>Applies this boss to a round and records it on the config.</summary>
    public RoundConfig Apply(RoundConfig config) => ModifyRound(config) with { Boss = this };
}

/// <summary>Seeded symmetric ink blots block cells, like a real crossword's black squares.</summary>
public sealed record InkSpill(int Pairs = 3) : BossModifier
{
    public override string Id => "ink-spill";
    public override string Name => "Ink Spill";
    public override string Description => $"{Pairs * 2} ink blots block the grid.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { BlockedPairs = Pairs };
}

/// <summary>A smaller grid: fewer lanes, fewer premiums, harder to build crossings.</summary>
public sealed record TightMargins(int Size = 5) : BossModifier
{
    public override string Id => "tight-margins";
    public override string Name => "Tight Margins";
    public override string Description => $"The grid shrinks to {Size}x{Size}.";

    protected override RoundConfig ModifyRound(RoundConfig config) =>
        config with { BoardSize = Size, Premiums = new PremiumPairs(TripleWord: 1, DoubleWord: 1, TripleLetter: 1, DoubleLetter: 1) };
}

/// <summary>
/// No 2-letter words — incidental short cross words become illegal, so parallel plays get hard. The rule alone is a
/// wall as the Week 5 finale (~45% of runs that reach it lost there), so its deadline is scaled by
/// <see cref="TargetScale"/> (0.75 → ~15%) to keep it the hardest boss without ending most runs.
/// </summary>
public sealed record StrictGrammarian(int MinLength = 3, decimal TargetScale = 0.75m) : BossModifier
{
    public override string Id => "strict-grammarian";
    public override string Name => "The Strict Grammarian";
    public override string Description => $"Every word formed must be at least {MinLength} letters.";

    protected override RoundConfig ModifyRound(RoundConfig config) =>
        config with { MinWordLength = MinLength, TargetScore = (long)(config.TargetScore * TargetScale) };
}

/// <summary>Vowels cost chips instead of earning them, in every word they appear in.</summary>
public sealed record VowelDrought(int ValuePerVowel = -1) : BossModifier
{
    private const string Vowels = "AEIOU";

    public override string Id => "vowel-drought";
    public override string Name => "Vowel Drought";
    public override string Description => $"Vowels are worth {ValuePerVowel} chips.";

    public override ScoringConfig ModifyScoring(ScoringConfig scoring) =>
        scoring with { LetterValues = scoring.LetterValues.SetItems(Vowels.Select(v => KeyValuePair.Create(v, ValuePerVowel))) };
}

/// <summary>One fewer submission to reach the deadline.</summary>
public sealed record TightDeadline(int Submissions = 3) : BossModifier
{
    public override string Id => "tight-deadline";
    public override string Name => "Tight Deadline";
    public override string Description => $"Only {Submissions} submissions this round.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { Submissions = Submissions };
}

/// <summary>Repeating yourself doesn't pay: a word already formed this round adds no letter chips (tier, intersections
/// and tile enhancements still count). Matched by text, so extending CAT to CATS is a new word.</summary>
public sealed record RedundantCopy : BossModifier
{
    public override string Id => "redundant-copy";
    public override string Name => "Redundant Copy";
    public override string Description => "Words already printed this round score no letter chips.";

    public override ScoringConfig ModifyScoring(ScoringConfig scoring) => scoring with { RepeatWordsScoreZero = true };
}

/// <summary>
/// Two other bosses at once. The catalog holds an unresolved template (no effect); <see cref="RunRules.BossFor"/>
/// resolves it per run and week with <see cref="Pick"/>, so the pair is seeded and previewable. Round changes apply
/// <see cref="First"/> then <see cref="Second"/>, then <see cref="TargetScale"/> scales the deadline.
/// </summary>
public sealed record PuzzleMaster(BossModifier? First = null, BossModifier? Second = null, decimal TargetScale = 1) : BossModifier
{
    public override string Id => "puzzle-master";
    public override string Name => "The Puzzle Master";

    public override string Description => First is null || Second is null
        ? "Two editors' rules at once."
        : $"Two editors at once. {First.Name}: {First.Description} {Second.Name}: {Second.Description}";

    protected override RoundConfig ModifyRound(RoundConfig config)
    {
        if (First is null || Second is null)
            return config;
        var both = Second.Apply(First.Apply(config));
        return TargetScale == 1 ? both : both with { TargetScore = (long)(both.TargetScore * TargetScale) };
    }

    public override ScoringConfig ModifyScoring(ScoringConfig scoring) =>
        Second?.ModifyScoring(First?.ModifyScoring(scoring) ?? scoring) ?? scoring;

    /// <summary>This boss with two different bosses drawn from <paramref name="candidates"/>.</summary>
    public PuzzleMaster Pick(IReadOnlyList<BossModifier> candidates, Rng rng)
    {
        var (first, next) = rng.NextInt(candidates.Count);
        var (second, _) = next.NextInt(candidates.Count - 1);
        if (second >= first)
            second++;
        return this with { First = candidates[first], Second = candidates[second] };
    }
}

/// <summary>A pool of bosses used from week index <see cref="FirstWeek"/> until the next tier starts.</summary>
public sealed record BossTier(string Name, int FirstWeek, ImmutableArray<BossModifier> Bosses);

public static class BossCatalog
{
    public static ImmutableArray<BossModifier> All { get; } =
    [
        new InkSpill(),
        new TightMargins(),
        new StrictGrammarian(),
        new VowelDrought(),
        new TightDeadline(),
        new RedundantCopy(),
        new PuzzleMaster(),
    ];

    /// <summary>The bosses The Puzzle Master pairs up: every Early and Mid boss (never The Strict Grammarian).</summary>
    public static ImmutableArray<BossModifier> PuzzleMasterCandidates { get; } =
    [
        new InkSpill(),
        new TightMargins(),
        new VowelDrought(),
        new TightDeadline(),
        new RedundantCopy(),
    ];

    /// <summary>
    /// Bosses get harder through the run. Ordered by measured difficulty (evaluating-bot boss loss rates):
    /// Ink Spill / Tight Margins ~3–4%, Vowel Drought ~4%, Tight Deadline ~9%, The Strict Grammarian ~12%.
    /// </summary>
    public static ImmutableArray<BossTier> DefaultTiers { get; } =
    [
        new BossTier("Early", FirstWeek: 0, [new InkSpill(), new TightMargins()]),
        new BossTier("Mid", FirstWeek: 2, [new VowelDrought(), new TightDeadline(), new RedundantCopy()]),
        new BossTier("Final", FirstWeek: 4, [new StrictGrammarian(), new PuzzleMaster()]),
    ];
}
