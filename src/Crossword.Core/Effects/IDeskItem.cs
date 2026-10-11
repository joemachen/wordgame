using Crossword.Core.Domain;
using Crossword.Core.Rules;

namespace Crossword.Core.Effects;

/// <summary>
/// Shop tier of a Desk Item: sets its price and how often it is offered (<see cref="Run.ShopConfig"/>). Epic and
/// Legendary (2026-10-10) are the run-defining items: rare in the shop, expensive, and few.
/// </summary>
/// <summary>
/// What a Desk Item does to a play's score. Items fire left to right, so the kind tells the player whether its slot
/// matters: a ×Mult item multiplies only the mult added before it (as in Balatro). Shown as a tag on the desk card.
/// </summary>
public enum DeskItemKind
{
    Chips,
    Mult,
    TimesMult,
    TimesChipsAndMult,
    Money,
    Rules,
}

/// <summary>Player-facing tag and explanation for each <see cref="DeskItemKind"/>.</summary>
public static class DeskItemKinds
{
    public static string Tag(DeskItemKind kind) => kind switch
    {
        DeskItemKind.Chips => "+CHIPS",
        DeskItemKind.Mult => "+MULT",
        DeskItemKind.TimesMult => "×MULT",
        DeskItemKind.TimesChipsAndMult => "×CHIPS ×MULT",
        DeskItemKind.Money => "$",
        _ => "RULES",
    };

    public static string Explain(DeskItemKind kind) => kind switch
    {
        DeskItemKind.Chips => "Adds chips: its place only matters next to a ×Chips item.",
        DeskItemKind.Mult => "Adds mult: put it to the left of ×Mult items so they multiply it.",
        DeskItemKind.TimesMult => "Multiplies the mult from everything to its left: put it after your +Mult items.",
        DeskItemKind.TimesChipsAndMult => "Multiplies chips and mult from everything to its left: put it last.",
        DeskItemKind.Money => "Pays money: its place doesn't matter.",
        _ => "Changes the round's rules: its place doesn't matter.",
    };
}

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

    /// <summary>What the item does to the score, which decides whether its slot matters (see <see cref="DeskItemKinds"/>).</summary>
    DeskItemKind Kind { get; }

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
