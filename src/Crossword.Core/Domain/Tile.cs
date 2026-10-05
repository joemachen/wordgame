namespace Crossword.Core.Domain;

/// <summary>
/// A physical tile in the player's deck. <see cref="Id"/> distinguishes duplicate letters
/// so future modifiers (enhancements, editions) can attach to a specific tile.
/// </summary>
public sealed record Tile(int Id, Letter Letter)
{
    public override string ToString() => Letter.ToString();
}
