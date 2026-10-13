namespace Crossword.Core.Effects;

/// <summary>
/// The best Desk Item order, kept automatically (2026-10-11, user's choice: players don't order cards by hand).
/// No item reads the running chips or mult — each only adds chips, adds mult, multiplies mult by a factor of at least 1,
/// multiplies chips and mult (Second Printing), pays money or changes the round's rules — so one order is best for
/// every play: +Chips, then +Mult, then money, rules and retriggers (a retrigger adds, then multiplies: after the
/// additions, before the multipliers), then ×Mult and ×Chips, then ×Chips ×Mult, then a Copy (× your best factor).
/// Adding a ≥ 0 before multiplying by k ≥ 1 never loses, since (m + a)·k ≥ m·k + a, and multiplications commute.
/// The sort is stable, so items of one kind keep the order they were bought in.
/// </summary>
public static class DeskOrder
{
    public static int Rank(DeskItemKind kind) => kind switch
    {
        DeskItemKind.Chips => 0,
        DeskItemKind.Mult => 1,
        DeskItemKind.Money or DeskItemKind.Rules or DeskItemKind.Retrigger => 2,
        DeskItemKind.TimesMult or DeskItemKind.TimesChips => 3,
        DeskItemKind.TimesChipsAndMult => 4,
        _ => 5,
    };

    public static System.Collections.Immutable.ImmutableArray<IDeskItem> Sorted(IEnumerable<IDeskItem> items) =>
        [.. items.OrderBy(item => Rank(item.Kind))];
}
