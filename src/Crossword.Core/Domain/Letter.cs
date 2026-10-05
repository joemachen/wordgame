namespace Crossword.Core.Domain;

/// <summary>A single uppercase A–Z letter. Construct via <see cref="From"/>.</summary>
public readonly record struct Letter
{
    public char Char { get; }

    private Letter(char c) => Char = c;

    public static Letter From(char c)
    {
        char upper = char.ToUpperInvariant(c);
        if (upper is < 'A' or > 'Z')
            throw new ArgumentOutOfRangeException(nameof(c), c, "Letter must be A-Z.");
        return new Letter(upper);
    }

    public override string ToString() => Char.ToString();
}
