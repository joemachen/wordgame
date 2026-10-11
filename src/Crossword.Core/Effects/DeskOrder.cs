namespace Crossword.Core.Effects;

/// <summary>
/// The best Desk Item order, kept automatically (2026-10-11, user's choice: players don't order cards by hand).
/// No item reads the running chips or mult — each only adds chips, adds mult, multiplies mult by a factor of at least 1,
/// multiplies chips and mult (Second Printing), pays money or changes the round's rules — so one order is best for
/// every play: +Chips, then +Mult, then money and rules (they don't touch the score), then ×Mult, then ×Chips ×Mult.
/// Adding a ≥ 0 before multiplying by k ≥ 1 never loses, since (m + a)·k ≥ m·k + a, and multiplications commute.
/// The sort is stable, so items of one kind keep the order they were bought in.
/// </summary>
public static class DeskOrder
{
    public static int Rank(DeskItemKind kind) => kind switch
    {
        DeskItemKind.Chips => 0,
        DeskItemKind.Mult => 1,
        DeskItemKind.Money or DeskItemKind.Rules => 2,
        DeskItemKind.TimesMult => 3,
        _ => 4,
    };

    public static System.Collections.Immutable.ImmutableArray<IDeskItem> Sorted(IEnumerable<IDeskItem> items) =>
        [.. items.OrderBy(item => Rank(item.Kind))];
}
