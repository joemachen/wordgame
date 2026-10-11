using Crossword.Core.Domain;
using Crossword.Core.Rules;

namespace Crossword.Core.Effects;

/// <summary>
/// Shop tier of a Desk Item: sets its price and how often it is offered (<see cref="Run.ShopConfig"/>). Epic and
/// Legendary (2026-10-10) are the run-defining items: rare in the shop, expensive, and few.
/// </summary>
public enum DeskItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

/// <summary>
/// A Desk Item (Balatro "Joker" equivalent). Implementations MUST be pure:
/// no mutation, no I/O, no hidden randomness. Log each firing via <see cref="ScoreContext.Record"/>;
/// when the item's condition isn't met, return the context unchanged (no log entry).
/// </summary>
public interface IDeskItem
{
    string Id { get; }

    string Name { get; }

    /// <summary>Player-facing rules text.</summary>
    string Description { get; }

    /// <summary>Sets the shop price (see ShopConfig).</summary>
    DeskItemRarity Rarity => DeskItemRarity.Common;

    ScoreContext Apply(ScoreContext context);

    /// <summary>Called after every submitted play. Scaling items return an updated copy; others return themselves.</summary>
    IDeskItem AfterPlay(PlayAnalysis play) => this;

    /// <summary>Called when a round is won (<paramref name="wasBoss"/> for Sunday Editions).</summary>
    IDeskItem AfterRoundWon(bool wasBoss) => this;

    /// <summary>
    /// Adjusts the rules of each round as it starts (hand size, blocked cells…), after the boss, in slot order.
    /// Never changes the deadline, so round previews stay accurate.
    /// </summary>
    RoundConfig ModifyRound(RoundConfig config) => config;
}
