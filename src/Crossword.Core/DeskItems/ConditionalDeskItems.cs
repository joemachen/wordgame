using Crossword.Core.Domain;
using Crossword.Core.Effects;

namespace Crossword.Core.DeskItems;

// Desk Items whose effect depends on the play or on run resources (ScoreContext.Env).

/// <summary>Rewards holding discards back.</summary>
public sealed record MarginNotes(decimal MultPerDiscard = 2) : IDeskItem
{
    public string Id => "margin-notes";
    public string Name => "Margin Notes";
    public string Description => $"+{MultPerDiscard} Mult per discard remaining.";

    public ScoreContext Apply(ScoreContext c)
    {
        if (c.Env.DiscardsLeft <= 0)
            return c;
        decimal mult = c.Env.DiscardsLeft * MultPerDiscard;
        return c.AddMult(mult).Record(Id, $"{Name}: +{mult} mult");
    }
}

/// <summary>Makes vowel-heavy plays worth chips.</summary>
public sealed record VowelSound(long ChipsPerVowel = 6) : IDeskItem
{
    private const string Vowels = "AEIOU";

    public string Id => "vowel-sound";
    public string Name => "Vowel Sound";
    public string Description => $"+{ChipsPerVowel} Chips per vowel tile placed this play.";

    public ScoreContext Apply(ScoreContext c)
    {
        int vowels = c.Play.Placed.Count(p => Vowels.Contains(p.Tile.Letter.Char));
        if (vowels == 0)
            return c;
        long chips = vowels * ChipsPerVowel;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Counterpart to Broadsheet: rewards short, dense plays.</summary>
public sealed record ShortStory(int MaxLength = 4, decimal Mult = 4) : IDeskItem
{
    public string Id => "short-story";
    public string Name => "Short Story";
    public string Description => $"+{Mult} Mult if the longest word has {MaxLength} or fewer letters.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.LongestWord.Length <= MaxLength ? c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult") : c;
}

public sealed record GridLock(long ChipsPerIntersection = 15) : IDeskItem
{
    public string Id => "grid-lock";
    public string Name => "Grid Lock";
    public string Description => $"+{ChipsPerIntersection} Chips per intersection.";

    public ScoreContext Apply(ScoreContext c)
    {
        int count = c.Play.Intersections.Length;
        if (count == 0)
            return c;
        long chips = count * ChipsPerIntersection;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Turns savings into chips — competes with spending in the shop.</summary>
public sealed record SavingsBond(long ChipsPerDollar = 2) : IDeskItem
{
    public string Id => "savings-bond";
    public string Name => "Savings Bond";
    public string Description => $"+{ChipsPerDollar} Chips per $1 you hold.";

    public ScoreContext Apply(ScoreContext c)
    {
        if (c.Env.MoneyHeld <= 0)
            return c;
        long chips = c.Env.MoneyHeld * ChipsPerDollar;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Big payoff for saving your best play for last.</summary>
public sealed record DeadlineRush(decimal Factor = 2) : IDeskItem
{
    public string Id => "deadline-rush";
    public string Name => "Deadline Rush";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"×{Factor} Mult on the round's final submission.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Env.SubmissionsLeft == 1 ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult") : c;
}

/// <summary>Rewards routing plays through word premiums.</summary>
public sealed record PremiumStock(decimal Mult = 4) : IDeskItem
{
    public string Id => "premium-stock";
    public string Name => "Premium Stock";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"+{Mult} Mult if a tile is placed on a 2W or 3W square.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.Placed.Any(p => c.Play.BoardAfter.PremiumAt(p.Position) is Premium.DoubleWord or Premium.TripleWord)
            ? c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult")
            : c;
}

/// <summary>Economy item: crossings pay money.</summary>
public sealed record Syndication(int DollarsPerIntersection = 1) : IDeskItem
{
    public string Id => "syndication";
    public string Name => "Syndication";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"Earn ${DollarsPerIntersection} per intersection.";

    public ScoreContext Apply(ScoreContext c)
    {
        int count = c.Play.Intersections.Length;
        if (count == 0)
            return c;
        int dollars = count * DollarsPerIntersection;
        return c.AddMoney(dollars).Record(Id, $"{Name}: +${dollars}");
    }
}

/// <summary>Rare multiplier for plays that form many words at once.</summary>
public sealed record EditorInChief(int MinWords = 3, decimal Factor = 3) : IDeskItem
{
    public string Id => "editor-in-chief";
    public string Name => "Editor-in-Chief";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×{Factor} Mult if the play forms {MinWords}+ words.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.Words.Length >= MinWords ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult") : c;
}
