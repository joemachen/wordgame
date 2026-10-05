namespace Crossword.Core.Domain;

/// <summary>
/// Permanent tile upgrades bought in the shop. Each triggers once per word formed in a play that
/// contains the tile — including tiles already on the board — so enhanced tiles at crossings pay twice.
/// </summary>
public enum TileEnhancement
{
    None,
    Bold,
    Italic,
    Gilded,
}

/// <summary>
/// A physical tile in the player's deck. <see cref="Id"/> distinguishes duplicate letters
/// so modifiers like <see cref="Enhancement"/> attach to a specific tile.
/// </summary>
public sealed record Tile(int Id, Letter Letter, TileEnhancement Enhancement = TileEnhancement.None)
{
    public override string ToString() => Letter.ToString();
}
