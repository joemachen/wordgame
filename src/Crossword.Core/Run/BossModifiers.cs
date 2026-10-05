using System.Collections.Immutable;
using Crossword.Core.Domain;
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

/// <summary>No 2-letter words — incidental short cross words become illegal, so parallel plays get hard.</summary>
public sealed record StrictGrammarian(int MinLength = 3) : BossModifier
{
    public override string Id => "strict-grammarian";
    public override string Name => "The Strict Grammarian";
    public override string Description => $"Every word formed must be at least {MinLength} letters.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { MinWordLength = MinLength };
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
    ];

    /// <summary>
    /// Bosses get harder through the run. Ordered by measured difficulty (evaluating-bot boss loss rates):
    /// Ink Spill / Tight Margins ~3–4%, Vowel Drought ~4%, Tight Deadline ~9%, The Strict Grammarian ~12%.
    /// </summary>
    public static ImmutableArray<BossTier> DefaultTiers { get; } =
    [
        new BossTier("Early", FirstWeek: 0, [new InkSpill(), new TightMargins()]),
        new BossTier("Mid", FirstWeek: 2, [new VowelDrought(), new TightDeadline()]),
        new BossTier("Final", FirstWeek: 4, [new StrictGrammarian()]),
    ];
}
