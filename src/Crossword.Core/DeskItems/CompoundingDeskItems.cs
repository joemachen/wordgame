using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;

namespace Crossword.Core.DeskItems;

// The compounding batch (2026-10-12, "break the game"): items whose effect multiplies with the play — per word, per
// tile, per intersection, per play — or with the run, so a build can push scores into the billions. Every factor
// depends on the play or the run environment, never on the running chips or mult, so the automatic desk order
// (DeskOrder) stays optimal.

/// <summary>Rare: ×Mult per word formed — parallel plays that form many words compound.</summary>
public sealed record ColumnInches(decimal PerWord = 2m) : IDeskItem
{
    public string Id => "column-inches";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Column Inches";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×{PerWord} Mult for every word formed (two words ×{PerWord * PerWord:0.##}, three ×{PerWord * PerWord * PerWord:0.##}…).";

    public ScoreContext Apply(ScoreContext c)
    {
        decimal factor = Compound.Power(PerWord, c.Play.Words.Length);
        return c.TimesMult(factor).Record(Id, $"{Name}: {c.Play.Words.Length} word(s), ×{factor:0.##} mult");
    }
}

/// <summary>Rare: ×Mult per tile placed this play — big plays compound.</summary>
public sealed record HotType(decimal PerTile = 1.3m) : IDeskItem
{
    public string Id => "hot-type";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Hot Type";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×{PerTile} Mult for every tile placed this play.";

    public ScoreContext Apply(ScoreContext c)
    {
        decimal factor = Compound.Power(PerTile, c.Play.Placed.Length);
        return c.TimesMult(factor).Record(Id, $"{Name}: {c.Play.Placed.Length} tile(s), ×{factor:0.##} mult");
    }
}

/// <summary>Rare: ×Mult per intersection — the grid-building multiplier.</summary>
public sealed record Interlock(decimal PerIntersection = 1.6m) : IDeskItem
{
    public string Id => "interlock";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Interlock";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×{PerIntersection} Mult for every intersection in the play.";

    public ScoreContext Apply(ScoreContext c)
    {
        int count = c.Play.Intersections.Length;
        if (count == 0)
            return c;
        decimal factor = Compound.Power(PerIntersection, count);
        return c.TimesMult(factor).Record(Id, $"{Name}: {count} intersection(s), ×{factor:0.##} mult");
    }
}

/// <summary>Uncommon: ×Mult per play already made this round — Late Edition's multiplicative sibling.</summary>
public sealed record EveningEdition(decimal PerSubmission = 2m) : IDeskItem
{
    public string Id => "evening-edition";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Evening Edition";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"×{PerSubmission} Mult for every play already made this round (first play ×1, second ×{PerSubmission}, third ×{PerSubmission * PerSubmission:0.##}…).";

    public ScoreContext Apply(ScoreContext c)
    {
        int made = c.Env.SubmissionsMade;
        if (made == 0)
            return c;
        decimal factor = Compound.Power(PerSubmission, made);
        return c.TimesMult(factor).Record(Id, $"{Name}: play {made + 1}, ×{factor:0.##} mult");
    }
}

/// <summary>Rare: long words multiply — ×Mult grows with every letter of the longest word beyond a few.</summary>
public sealed record FeatureStory(decimal PerLetter = 1m, int BeyondLength = 4) : IDeskItem
{
    public string Id => "feature-story";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Feature Story";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×(1 + {PerLetter} per letter of the longest word beyond {BeyondLength}): a {BeyondLength + 2}-letter word ×{1 + 2 * PerLetter:0.##}, {BeyondLength + 3} letters ×{1 + 3 * PerLetter:0.##}.";

    public ScoreContext Apply(ScoreContext c)
    {
        int extra = c.Play.LongestWord.Length - BeyondLength;
        if (extra <= 0)
            return c;
        decimal factor = 1 + PerLetter * extra;
        return c.TimesMult(factor).Record(Id, $"{Name}: {c.Play.LongestWord.Length} letters, ×{factor:0.##} mult");
    }
}

/// <summary>Uncommon: money multiplies — ×Mult grows with the money held.</summary>
public sealed record AdRevenue(decimal PerStep = 0.2m, int MoneyStep = 5) : IDeskItem
{
    public string Id => "ad-revenue";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Ad Revenue";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"×(1 + {PerStep} per ${MoneyStep} held): ${MoneyStep * 4} held is ×{1 + 4 * PerStep:0.##} Mult.";

    public ScoreContext Apply(ScoreContext c)
    {
        int steps = Math.Max(0, c.Env.MoneyHeld) / MoneyStep;
        if (steps == 0)
            return c;
        decimal factor = 1 + PerStep * steps;
        return c.TimesMult(factor).Record(Id, $"{Name}: ${c.Env.MoneyHeld} held, ×{factor:0.##} mult");
    }
}

/// <summary>Epic: compounding scaler — its ×Mult grows by a factor with every play that forms several words, forever.</summary>
public sealed record SubscriptionDrive(decimal Growth = 1.15m, int MinWords = 3, decimal Factor = 1) : IDeskItem
{
    public string Id => "subscription-drive";
    public DeskItemKind Kind => DeskItemKind.TimesMult;
    public string Name => "Subscription Drive";
    public DeskItemRarity Rarity => DeskItemRarity.Epic;
    public string Description => $"×{Factor:0.##} Mult. Grows ×{Growth} every play that forms {MinWords}+ words, without limit.";

    public ScoreContext Apply(ScoreContext c) =>
        Factor > 1 ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor:0.##} mult") : c;

    public IDeskItem AfterPlay(PlayAnalysis play) =>
        play.Words.Length >= MinWords ? this with { Factor = ScoreMath.MultiplyMult(Factor, Growth) } : this;
}

/// <summary>Rare: ×Chips per word formed — the chips side of Column Inches.</summary>
public sealed record Newsstand(decimal PerWord = 0.5m) : IDeskItem
{
    public string Id => "newsstand";
    public DeskItemKind Kind => DeskItemKind.TimesChips;
    public string Name => "Newsstand";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"Chips ×(1 + {PerWord} per word formed): one word ×{1 + PerWord:0.##}, three words ×{1 + 3 * PerWord:0.##}.";

    public ScoreContext Apply(ScoreContext c)
    {
        decimal factor = 1 + PerWord * c.Play.Words.Length;
        return c.TimesChips(factor).Record(Id, $"{Name}: {c.Play.Words.Length} word(s), ×{factor:0.##} chips");
    }
}

/// <summary>
/// Uncommon: the longest word's enhanced tiles fire again (Bold, Italic, Gilded, Underlined, Embossed) — the retrigger
/// that makes an enhanced deck compound. Its additive part comes before the ×Mult items and its multiplicative part after
/// the +Mult items, which is where <see cref="DeskOrder"/> puts a Retrigger.
/// </summary>
public sealed record PullQuote : IDeskItem
{
    public string Id => "pull-quote";
    public DeskItemKind Kind => DeskItemKind.Retrigger;
    public string Name => "Pull Quote";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => "The enhanced tiles of the longest word fire again.";

    public ScoreContext Apply(ScoreContext c)
    {
        var word = c.Play.LongestWord;
        if (!word.Cells.Any(cell => cell.Tile.Enhancement != TileEnhancement.None))
            return c;
        var scoring = c.Scoring ?? ScoringConfig.Default;
        c = ScoringEngine.ApplyEnhancements(c, word, scoring, multiplicative: false, Id, $"{Name}: ");
        return ScoringEngine.ApplyEnhancements(c, word, scoring, multiplicative: true, Id, $"{Name}: ");
    }
}

/// <summary>
/// Epic: fires your strongest multiplier again — whichever owned ×Mult or ×Chips ×Mult item gives the most at this point
/// of the play. A pure "× your best factor", so it sits last on the desk (<see cref="DeskItemKind.Copy"/>).
/// </summary>
public sealed record CarbonCopy : IDeskItem
{
    public string Id => "carbon-copy";
    public DeskItemKind Kind => DeskItemKind.Copy;
    public string Name => "Carbon Copy";
    public DeskItemRarity Rarity => DeskItemRarity.Epic;
    public string Description => "Your strongest ×Mult or ×Chips ×Mult Desk Item fires a second time.";

    /// <summary>Without a desk to read there is nothing to copy.</summary>
    public ScoreContext Apply(ScoreContext c) => c;

    public ScoreContext Apply(ScoreContext c, IReadOnlyList<IDeskItem> desk)
    {
        IDeskItem? best = null;
        ScoreContext? bestResult = null;
        foreach (var item in desk)
        {
            if (item.Kind is not (DeskItemKind.TimesMult or DeskItemKind.TimesChips or DeskItemKind.TimesChipsAndMult))
                continue;
            var result = item.Apply(c with { Log = System.Collections.Immutable.ImmutableList<EffectEvent>.Empty });
            if (result.Total > c.Total && (bestResult is null || result.Total > bestResult.Total))
                (best, bestResult) = (item, result);
        }
        return best is null || bestResult is null
            ? c
            : (c with { Chips = bestResult.Chips, Mult = bestResult.Mult, Money = bestResult.Money }).Record(Id, $"{Name}: {best.Name} again");
    }
}

/// <summary>Decimal powers for the compounding items (no floating point, saturating).</summary>
public static class Compound
{
    public static decimal Power(decimal factor, int times)
    {
        decimal result = 1;
        for (int i = 0; i < times; i++)
            result = ScoreMath.MultiplyMult(result, factor);
        return result;
    }
}
