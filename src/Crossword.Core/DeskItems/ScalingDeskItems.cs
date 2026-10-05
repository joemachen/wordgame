using Crossword.Core.Effects;
using Crossword.Core.Rules;

namespace Crossword.Core.DeskItems;

// Desk Items that grow over a run. Growth happens in AfterPlay/AfterRoundWon, which return an updated copy,
// so the play that triggers growth is scored with the old value.

/// <summary>Grows Chips with every tile you place.</summary>
public sealed record WordCount(long ChipsPerTile = 2, long Chips = 0) : IDeskItem
{
    public string Id => "word-count";
    public string Name => "Word Count";
    public string Description => $"Gains +{ChipsPerTile} Chips per tile placed (currently +{Chips} Chips).";

    public ScoreContext Apply(ScoreContext c) =>
        Chips > 0 ? c.AddChips(Chips).Record(Id, $"{Name}: +{Chips} chips") : c;

    public IDeskItem AfterPlay(PlayAnalysis play) => this with { Chips = Chips + ChipsPerTile * play.Placed.Length };
}

/// <summary>Grows Mult every time you land a long word.</summary>
public sealed record Archive(int MinLength = 5, decimal MultPerLongWord = 1, decimal Mult = 0) : IDeskItem
{
    public string Id => "archive";
    public string Name => "Archive";
    public DeskItemRarity Rarity => DeskItemRarity.Uncommon;
    public string Description => $"Gains +{MultPerLongWord} Mult per play with a {MinLength}+ letter word (currently +{Mult} Mult).";

    public ScoreContext Apply(ScoreContext c) =>
        Mult > 0 ? c.AddMult(Mult).Record(Id, $"{Name}: +{Mult} mult") : c;

    public IDeskItem AfterPlay(PlayAnalysis play) =>
        play.LongestWord.Length >= MinLength ? this with { Mult = Mult + MultPerLongWord } : this;
}

/// <summary>Rare multiplicative scaler that grows with each Sunday Edition cleared.</summary>
public sealed record Pulitzer(decimal Factor = 1.5m, decimal PerBoss = 0.5m) : IDeskItem
{
    public string Id => "pulitzer";
    public string Name => "Pulitzer";
    public DeskItemRarity Rarity => DeskItemRarity.Rare;
    public string Description => $"×{Factor} Mult. Gains ×{PerBoss} per Sunday Edition cleared.";

    public ScoreContext Apply(ScoreContext c) => c.TimesMult(Factor).Record(Id, $"{Name}: ×{Factor} mult");

    public IDeskItem AfterRoundWon(bool wasBoss) => wasBoss ? this with { Factor = Factor + PerBoss } : this;
}
