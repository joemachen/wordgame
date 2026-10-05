namespace Crossword.Core.Effects;

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

    ScoreContext Apply(ScoreContext context);
}
