using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;

namespace Crossword.Core.DeskItems;

// Phase 2 Desk Items (ROADMAP §3).

/// <summary>Counts the longest word's letter chips a second time (rare letters and premiums included).</summary>
public sealed record EtymologyTome : IDeskItem
{
    public string Id => "etymology-tome";
    public string Name => "Etymology Tome";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => "The longest word's letter chips count twice.";

    public ScoreContext Apply(ScoreContext c)
    {
        int index = c.Play.Words.IndexOf(c.Play.LongestWord);
        long chips = index >= 0 && index < c.WordChips.Length ? c.WordChips[index] : 0;
        return chips > 0 ? c.AddChips(chips).Record(Id, $"{Name}: {c.Play.LongestWord.Text} again, +{chips} chips") : c;
    }
}

/// <summary>Multiplies the round's opening play.</summary>
public sealed record RubberStamp(decimal Factor = 2) : IDeskItem
{
    public string Id => "rubber-stamp";
    public string Name => "Rubber Stamp";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"×{Factor} Mult on the first submission of each round.";

    public ScoreContext Apply(ScoreContext c) =>
        c.Env.SubmissionsMade == 0 ? c.TimesMult(Factor).Record(Id, $"{Name}: first play, ×{Factor} mult") : c;
}

/// <summary>Rare multiplicative scaler that grows with plays forming many words.</summary>
public sealed record PrintingPressRoller(int MinWords = 3, decimal Gain = 0.1m, decimal Factor = 1) : IDeskItem
{
    public string Id => "printing-press-roller";
    public string Name => "Printing Press Roller";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"Gains ×{Gain} Mult per play forming {MinWords}+ words (currently ×{Factor} Mult).";

    public ScoreContext Apply(ScoreContext c) =>
        Factor > 1 ? c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult") : c;

    public IDeskItem AfterPlay(PlayAnalysis play) =>
        play.Words.Length >= MinWords ? this with { Factor = Factor + Gain } : this;
}

/// <summary>One more tile in hand every round.</summary>
public sealed record TileRack(int ExtraTiles = 1) : IDeskItem
{
    public string Id => "tile-rack";
    public string Name => "Tile Rack";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"+{ExtraTiles} hand size.";

    public ScoreContext Apply(ScoreContext c) => c;

    public RoundConfig ModifyRound(RoundConfig config) => config with { HandSize = config.HandSize + ExtraTiles };
}

/// <summary>Flat Mult at the cost of a stained (blocked) pair of squares every round.</summary>
public sealed record CoffeeStain(decimal Mult = 4, int StainedPairs = 1) : IDeskItem
{
    public string Id => "coffee-stain";
    public string Name => "Coffee Stain";
    public string Description => $"+{Mult} Mult. Stains {StainedPairs * 2} mirrored squares each round (blocked).";

    public ScoreContext Apply(ScoreContext c) => c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult");

    public RoundConfig ModifyRound(RoundConfig config) => config with { BlockedPairs = config.BlockedPairs + StainedPairs };
}
