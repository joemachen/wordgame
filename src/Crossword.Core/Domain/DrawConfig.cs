namespace Crossword.Core.Domain;

/// <summary>
/// Guard rails for drawing into the hand. Draws stay random; these only steer the last slots of a refill when a
/// guarantee would otherwise fail, and skip extra copies of a vowel already held. 0 turns a rule off. Vowels are
/// A E I O U (Y counts as a consonant). All best effort: when the bag can't satisfy a rule, the draw is plain random.
/// </summary>
/// <param name="MinVowels">The hand gets at least this many vowels after a refill (when the bag allows).</param>
/// <param name="MinConsonants">The hand gets at least this many consonants after a refill (when the bag allows).</param>
/// <param name="MaxCopiesPerVowel">A refill never brings a vowel past this many copies in the hand.</param>
public sealed record DrawConfig(int MinVowels = 0, int MinConsonants = 0, int MaxCopiesPerVowel = 0)
{
    /// <summary>Plain uniform draws.</summary>
    public static DrawConfig Off { get; } = new();

    /// <summary>The default: at least 2 vowels and 2 consonants, no vowel more than twice.</summary>
    public static DrawConfig Balanced { get; } = new(MinVowels: 2, MinConsonants: 2, MaxCopiesPerVowel: 2);

    public bool IsOff => MinVowels <= 0 && MinConsonants <= 0 && MaxCopiesPerVowel <= 0;

    public static bool IsVowel(Letter letter) => letter.Char is 'A' or 'E' or 'I' or 'O' or 'U';
}
