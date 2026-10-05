using Crossword.Core.Domain;
using Crossword.Core.Effects;

namespace Crossword.Core.Run;

public enum RunPhase
{
    InRound,
    Shop,
    Victory,
    Defeat,
}

/// <summary>
/// Root immutable state of a game in progress: persistent run data, the current (or last) round,
/// and the shop when open. All transitions live in <see cref="RunRules"/> and <see cref="ShopRules"/>.
/// </summary>
public sealed record GameSession(
    RunConfig Config,
    RunState Run,
    RunPhase Phase,
    RoundState Round,
    ShopState? Shop = null,
    Payout? LastPayout = null)
{
    public int Week => Config.WeekOf(Run.RoundIndex);

    public RoundKind Kind => Config.KindOf(Run.RoundIndex);

    /// <summary>The boss waiting at the end of the current week (known in advance, like Balatro).</summary>
    public BossModifier WeekBoss => RunRules.BossFor(Run, Week);
}

public sealed record SessionOutcome(GameSession Session, ScoreContext Score);
