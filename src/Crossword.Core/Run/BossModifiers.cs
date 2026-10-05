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

/// <summary>Seeded symmetric black squares break up the grid, like a real crossword.</summary>
public sealed record BlackSquares(int Pairs = 4) : BossModifier
{
    public override string Id => "black-squares";
    public override string Name => "Black Squares";
    public override string Description => $"{Pairs * 2} black squares block the grid.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { BlockedPairs = Pairs };
}

public sealed record PocketEdition(int Size = 5) : BossModifier
{
    public override string Id => "pocket-edition";
    public override string Name => "Pocket Edition";
    public override string Description => $"The grid shrinks to {Size}x{Size}.";

    protected override RoundConfig ModifyRound(RoundConfig config) =>
        config with { BoardSize = Size, Premiums = new PremiumPairs(TripleWord: 1, DoubleWord: 1, TripleLetter: 1, DoubleLetter: 1) };
}

/// <summary>No 2-letter words — incidental short cross words become illegal, so parallel plays get hard.</summary>
public sealed record StrictEditor(int MinLength = 3) : BossModifier
{
    public override string Id => "strict-editor";
    public override string Name => "Strict Editor";
    public override string Description => $"Every word formed must be at least {MinLength} letters.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { MinWordLength = MinLength };
}

public sealed record VowelTax : BossModifier
{
    private const string Vowels = "AEIOU";

    public override string Id => "vowel-tax";
    public override string Name => "Vowel Tax";
    public override string Description => "Vowels score 0 chips.";

    public override ScoringConfig ModifyScoring(ScoringConfig scoring) =>
        scoring with { LetterValues = scoring.LetterValues.SetItems(Vowels.Select(v => KeyValuePair.Create(v, 0))) };
}

public sealed record TightDeadline(int Submissions = 3) : BossModifier
{
    public override string Id => "tight-deadline";
    public override string Name => "Tight Deadline";
    public override string Description => $"Only {Submissions} submissions this round.";

    protected override RoundConfig ModifyRound(RoundConfig config) => config with { Submissions = Submissions };
}

public static class BossCatalog
{
    public static ImmutableArray<BossModifier> All { get; } =
    [
        new BlackSquares(),
        new PocketEdition(),
        new StrictEditor(),
        new VowelTax(),
        new TightDeadline(),
    ];
}
