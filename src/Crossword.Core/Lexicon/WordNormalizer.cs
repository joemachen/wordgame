namespace Crossword.Core.Lexicon;

public static class WordNormalizer
{
    public const int MinLength = 2;
    public const int MaxLength = 15;

    /// <summary>
    /// Trims and uppercases <paramref name="raw"/>. Returns false unless the result is
    /// A–Z only and between <see cref="MinLength"/> and <see cref="MaxLength"/> letters.
    /// </summary>
    public static bool TryNormalize(string? raw, out string word)
    {
        word = string.Empty;
        if (raw is null)
            return false;

        var trimmed = raw.AsSpan().Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
            return false;

        Span<char> buffer = stackalloc char[trimmed.Length];
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = char.ToUpperInvariant(trimmed[i]);
            if (c is < 'A' or > 'Z')
                return false;
            buffer[i] = c;
        }

        word = new string(buffer);
        return true;
    }
}
