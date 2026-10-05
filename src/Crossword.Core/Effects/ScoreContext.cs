using System.Collections.Immutable;
using Crossword.Core.Rules;

namespace Crossword.Core.Effects;

/// <summary>Read-only facts about the run at the moment of a play, for items that react to resources.</summary>
/// <param name="SubmissionsLeft">Submissions left including the one being played (1 = final submission).</param>
public sealed record ScoreEnvironment(int MoneyHeld, int SubmissionsLeft, int DiscardsLeft)
{
    public static ScoreEnvironment Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Running Chips × Mult for a single play, threaded through scoring steps and Desk Items.
/// <see cref="Play"/> gives effects read access to the words, tiles, and board of the play.
/// </summary>
public sealed record ScoreContext(long Chips, decimal Mult, PlayAnalysis Play, ImmutableList<EffectEvent> Log)
{
    public static ScoreContext Start(PlayAnalysis play, long chips = 0, decimal mult = 0) =>
        new(chips, mult, play, ImmutableList<EffectEvent>.Empty);

    public ScoreEnvironment Env { get; init; } = ScoreEnvironment.Empty;

    /// <summary>Money earned during this play (e.g. Gilded tiles); paid into the run immediately.</summary>
    public int Money { get; init; }

    /// <summary>Final play score: floor(Chips × Mult), never negative (boss rules can push Chips below zero).</summary>
    public long Total => Math.Max(0, (long)decimal.Floor(Chips * Mult));

    public ScoreContext AddChips(long amount) => this with { Chips = Chips + amount };

    public ScoreContext AddMult(decimal amount) => this with { Mult = Mult + amount };

    public ScoreContext TimesMult(decimal factor) => this with { Mult = Mult * factor };

    public ScoreContext AddMoney(int amount) => this with { Money = Money + amount };

    /// <summary>Appends an event snapshotting the current Chips/Mult, for UI playback.</summary>
    public ScoreContext Record(string sourceId, string description) =>
        this with { Log = Log.Add(new EffectEvent(sourceId, description, Chips, Mult)) };
}

/// <summary>One scoring step or effect firing, with the running totals after it applied.</summary>
public sealed record EffectEvent(string SourceId, string Description, long ChipsAfter, decimal MultAfter)
{
    public override string ToString() => $"{Description}  →  {ChipsAfter} × {MultAfter:0.##}";
}
