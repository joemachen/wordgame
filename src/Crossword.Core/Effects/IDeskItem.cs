namespace Crossword.Core.Effects;

/// <summary>
/// A Desk Item (Balatro "Joker" equivalent). Implementations MUST be pure:
/// no mutation, no I/O, no hidden randomness. Log each firing via <see cref="ScoreContext.Record"/>.
/// </summary>
public interface IDeskItem
{
    string Id { get; }

    string Name { get; }

    ScoreContext Apply(ScoreContext context);
}
