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
/// A <see cref="IsWild"/> tile can be played as any letter: in the deck, bag and hand its <see cref="Letter"/> is just
/// <see cref="WildPlaceholder"/>; when placed it carries the chosen letter (<see cref="As"/>). Wilds score 0 letter
/// chips but count for everything else (length, tier, premiums, intersections, enhancements).
/// </summary>
public sealed record Tile(int Id, Letter Letter, TileEnhancement Enhancement = TileEnhancement.None, bool IsWild = false)
{
    /// <summary>The meaningless letter an unplaced wild tile carries.</summary>
    public static Letter WildPlaceholder { get; } = Letter.From('A');

    public static Tile Wild(int id, TileEnhancement enhancement = TileEnhancement.None) =>
        new(id, WildPlaceholder, enhancement, IsWild: true);

    /// <summary>This wild tile played as <paramref name="letter"/>.</summary>
    public Tile As(Letter letter) => IsWild
        ? this with { Letter = letter }
        : throw new InvalidOperationException($"Tile {Id} is not wild.");

    /// <summary>"?" for a wild tile (its letter is only chosen when placed).</summary>
    public override string ToString() => IsWild ? "?" : Letter.ToString();
}
