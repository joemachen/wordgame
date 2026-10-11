using Crossword.Core.Clues;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;

namespace Crossword.Core.DeskItems;

// The newsroom batch (2026-10-10, playtest ask for "more varied and fun desk items … some legendaries or epics"):
// twelve items across every rarity, filling gaps the first 23 left — short words, doubled letters, board edges,
// single-word plays, late submissions, the grid as a whole, every word's letters, letter variety over a run, and
// the first Chips multiplier and submission-granting item.

/// <summary>The small ads: short words pay.</summary>
public sealed record Classifieds(long ChipsPerWord = 8, int MaxLength = 3) : IDeskItem
{
    public string Id => "classifieds";
    public string Name => "Classifieds";
    public string Description => $"+{ChipsPerWord} Chips per word formed of {MaxLength} letters or fewer.";

    public ScoreContext Apply(ScoreContext c)
    {
        int shortWords = c.Play.Words.Count(w => w.Length <= MaxLength);
        if (shortWords == 0)
            return c;
        long chips = shortWords * ChipsPerWord;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Rewards the doubled letters that make hands feel bad.</summary>
public sealed record Typesetter(long ChipsPerPair = 12) : IDeskItem
{
    public string Id => "typesetter";
    public string Name => "Typesetter";
    public string Description => $"+{ChipsPerPair} Chips per pair of identical letters placed this play.";

    public ScoreContext Apply(ScoreContext c)
    {
        int pairs = c.Play.Placed.Where(p => !p.Tile.IsWild).GroupBy(p => p.Tile.Letter.Char).Sum(g => g.Count() / 2);
        if (pairs == 0)
            return c;
        long chips = pairs * ChipsPerPair;
        return c.AddChips(chips).Record(Id, $"{Name}: +{chips} chips");
    }
}

/// <summary>Pulls play out to the margins: a tile on the board's edge pays Mult.</summary>
public sealed record Byline(decimal Mult = 3) : IDeskItem
{
    public string Id => "byline";
    public string Name => "Byline";
    public string Description => $"+{Mult} Mult if a tile is placed on the edge of the board.";

    public ScoreContext Apply(ScoreContext c)
    {
        int last = c.Play.BoardAfter.Size - 1;
        bool onEdge = c.Play.Placed.Any(p => p.Position.Row == 0 || p.Position.Col == 0 || p.Position.Row == last || p.Position.Col == last);
        return onEdge ? c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult") : c;
    }
}

/// <summary>Counterpart to Inkwell and Editor-in-Chief: a clean single-word play.</summary>
public sealed record OpEd(decimal Mult = 5) : IDeskItem
{
    public string Id => "op-ed";
    public string Name => "Op-Ed";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"+{Mult} Mult if the play forms exactly one word.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Play.Words.Length == 1 ? c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult") : c;
}

/// <summary>Mirror of Rubber Stamp: every submission already made this round adds Mult.</summary>
public sealed record LateEdition(decimal MultPerSubmission = 2) : IDeskItem
{
    public string Id => "late-edition";
    public string Name => "Late Edition";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"+{MultPerSubmission} Mult per submission already made this round.";

    public ScoreContext Apply(ScoreContext c)
    {
        if (c.Env.SubmissionsMade <= 0)
            return c;
        decimal mult = c.Env.SubmissionsMade * MultPerSubmission;
        return c.AddMult(mult).Record(Id, $"{Name}: +{mult} mult");
    }
}

/// <summary>The newspaper's archive: the whole grid pays, so building it up matters.</summary>
public sealed record Morgue(long ChipsPerBoardWord = 4) : IDeskItem
{
    public string Id => "morgue";
    public string Name => "The Morgue";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"+{ChipsPerBoardWord} Chips per word on the board after the play.";

    public ScoreContext Apply(ScoreContext c)
    {
        int words = BoardWords.Numbered(c.Play.BoardAfter).Length;
        if (words == 0)
            return c;
        long chips = words * ChipsPerBoardWord;
        return c.AddChips(chips).Record(Id, $"{Name}: {words} words on the board, +{chips} chips");
    }
}

/// <summary>The Rare chips item: every letter of every word formed.</summary>
public sealed record Headline(long ChipsPerLetter = 6) : IDeskItem
{
    public string Id => "headline";
    public string Name => "Headline";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"+{ChipsPerLetter} Chips per letter of every word formed.";

    public ScoreContext Apply(ScoreContext c)
    {
        int letters = c.Play.Words.Sum(w => w.Length);
        if (letters == 0)
            return c;
        long chips = letters * ChipsPerLetter;
        return c.AddChips(chips).Record(Id, $"{Name}: {letters} letters, +{chips} chips");
    }
}

/// <summary>Scales with letter variety: every distinct letter placed over the run adds Mult (26 at most).</summary>
public sealed record LettersToTheEditor(decimal MultPerLetter = 0.5m, string Letters = "") : IDeskItem
{
    public string Id => "letters-to-the-editor";
    public string Name => "Letters to the Editor";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description =>
        $"Gains +{MultPerLetter} Mult per distinct letter placed this run (currently +{Letters.Length * MultPerLetter:0.##} Mult from {Letters.Length} letters).";

    public ScoreContext Apply(ScoreContext c)
    {
        if (Letters.Length == 0)
            return c;
        decimal mult = Letters.Length * MultPerLetter;
        return c.AddMult(mult).Record(Id, $"{Name}: {Letters.Length} letters, +{mult} mult");
    }

    public IDeskItem AfterPlay(PlayAnalysis play)
    {
        var letters = play.Placed.Where(p => !p.Tile.IsWild).Select(p => p.Tile.Letter.Char).Concat(Letters).Distinct().Order().ToArray();
        return letters.Length == Letters.Length ? this : this with { Letters = new string(letters) };
    }
}

/// <summary>Etymology Tome for every word: all letter chips count twice.</summary>
public sealed record FrontPage : IDeskItem
{
    public string Id => "front-page";
    public string Name => "Front Page";
    public DeskItemRarity Rarity => DeskItemRarity.Epic;
    public string Description => "Every word's letter chips count twice.";

    public ScoreContext Apply(ScoreContext c)
    {
        long chips = c.WordChips.Sum();
        return chips > 0 ? c.AddChips(chips).Record(Id, $"{Name}: all words again, +{chips} chips") : c;
    }
}

/// <summary>Rewards crossing what is already on the board: each old tile a formed word runs through adds Mult.</summary>
public sealed record CrosswordEditor(decimal MultPerReusedTile = 1) : IDeskItem
{
    public string Id => "crossword-editor";
    public string Name => "Crossword Editor";
    public DeskItemRarity Rarity => DeskItemRarity.Epic;
    public string Description => $"+{MultPerReusedTile} Mult per tile already on the board that a formed word runs through.";

    public ScoreContext Apply(ScoreContext c)
    {
        int reused = c.Play.Words.SelectMany(w => w.Cells).Where(cell => !cell.IsNew).Select(cell => cell.Position).Distinct().Count();
        if (reused == 0)
            return c;
        decimal mult = reused * MultPerReusedTile;
        return c.AddMult(mult).Record(Id, $"{Name}: {reused} tiles reused, +{mult} mult");
    }
}

/// <summary>Legendary: one more submission every round (never the deadline).</summary>
public sealed record ExtraExtra(int Submissions = 1) : IDeskItem
{
    public string Id => "extra-extra";
    public string Name => "Extra! Extra!";
    public DeskItemRarity Rarity => DeskItemRarity.Legendary;
    public string Description => $"+{Submissions} submission every round.";

    public ScoreContext Apply(ScoreContext c) => c;

    public RoundConfig ModifyRound(RoundConfig config) => config with { Submissions = config.Submissions + Submissions };
}

/// <summary>Legendary: the whole play printed twice — the first item that multiplies Chips, so +Chips items want to sit before it.</summary>
public sealed record SecondPrinting(decimal Factor = 2) : IDeskItem
{
    public string Id => "second-printing";
    public string Name => "Second Printing";
    public DeskItemRarity Rarity => DeskItemRarity.Legendary;
    public string Description => $"×{Factor} Chips and ×{Factor} Mult.";

    public ScoreContext Apply(ScoreContext c) =>
        (c with { Chips = (long)decimal.Floor(c.Chips * Factor) }).TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} chips, ×{Factor} mult");
}
