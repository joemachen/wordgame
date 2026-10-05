using Crossword.Core.Effects;

namespace Crossword.Core.DeskItems;

// Starter Desk Items. Numbers are constructor parameters so balance can be tuned without new types.
// Defaults tuned by simulation (skill-0.9 player, round 1, item alone): Red Pen/Thesaurus/Inkwell ~+50%,
// Cross-Reference ~+43%, Broadsheet ~+34% (×Mult items scale further with other items), Rare Ink ~+9%
// (build-around: needs deck editing to shine).

/// <summary>Flat Mult — the simplest item, like a basic Joker.</summary>
public sealed record RedPen(decimal Mult = 2) : IDeskItem
{
    public string Id => "red-pen";
    public string Name => "Red Pen";
    public string Description => $"+{Mult} Mult.";

    public ScoreContext Apply(ScoreContext c) => c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult");
}

/// <summary>Rewards long words with Chips.</summary>
public sealed record Thesaurus(long ChipsPerLetter = 4) : IDeskItem
{
    public string Id => "thesaurus";
    public string Name => "Thesaurus";
    public string Description => $"+{ChipsPerLetter} Chips per letter of the longest word.";

    public ScoreContext Apply(ScoreContext c)
    {
        long chips = ChipsPerLetter * c.Play.LongestWord.Length;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Rewards plays that form several words at once (parallel plays, hooks).</summary>
public sealed record Inkwell(long ChipsPerExtraWord = 10) : IDeskItem
{
    public string Id => "inkwell";
    public string Name => "Inkwell";
    public string Description => $"+{ChipsPerExtraWord} Chips for each word formed beyond the first.";

    public ScoreContext Apply(ScoreContext c)
    {
        int extra = c.Play.Words.Length - 1;
        if (extra <= 0)
            return c;
        long chips = extra * ChipsPerExtraWord;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips ({extra} extra words)");
    }
}

/// <summary>Multiplicative payoff for dense crossing plays — the crossword "juice" item.</summary>
public sealed record CrossReference(int MinIntersections = 2, decimal Factor = 2) : IDeskItem
{
    public string Id => "cross-reference";
    public string Name => "Cross-Reference";
    public string Description => $"×{Factor} Mult if the play has {MinIntersections}+ intersections.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.Intersections.Length >= MinIntersections
            ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult")
            : c;
}

/// <summary>Big multiplier for long words; competes with intersection-focused builds.</summary>
public sealed record Broadsheet(int MinLength = 5, decimal Factor = 2) : IDeskItem
{
    public string Id => "broadsheet";
    public string Name => "Broadsheet";
    public string Description => $"×{Factor} Mult if the longest word has {MinLength}+ letters.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.LongestWord.Length >= MinLength
            ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult")
            : c;
}

/// <summary>Turns awkward high-value letters into a Mult engine.</summary>
public sealed record RareInk(decimal MultPerTile = 6) : IDeskItem
{
    private const string RareLetters = "JQXZ";

    public string Id => "rare-ink";
    public string Name => "Rare Ink";
    public string Description => $"+{MultPerTile} Mult for each J, Q, X or Z placed this play.";

    public ScoreContext Apply(ScoreContext c)
    {
        int count = c.Play.Placed.Count(p => RareLetters.Contains(p.Tile.Letter.Char));
        if (count == 0)
            return c;
        decimal mult = count * MultPerTile;
        return c.AddMult(mult).Record(Id, $"{Name}: +{mult} mult");
    }
}
