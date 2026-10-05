using System.Collections.Immutable;

namespace Crossword.Core.Effects;

/// <summary>
/// PLACEHOLDER scoring context threaded through the Desk Item pipeline.
/// Final shape (chips/mult/word data) will be designed with the scoring engine.
/// </summary>
public sealed record ScoreContext(long Base, long Multiplier, ImmutableList<EffectEvent> Log)
{
    public static ScoreContext Start(long @base, long multiplier = 1) =>
        new(@base, multiplier, ImmutableList<EffectEvent>.Empty);

    public long Total => Base * Multiplier;

    public ScoreContext Record(EffectEvent evt) => this with { Log = Log.Add(evt) };
}

/// <summary>A record of one effect firing; the engine replays these to drive animations.</summary>
public sealed record EffectEvent(string SourceId, string Description);
