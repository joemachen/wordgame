using Crossword.Core.Rules;

namespace Crossword.Core.Effects;

public enum DeskItemRarity
{
    Common,
    Uncommon,
    Rare,
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
}
