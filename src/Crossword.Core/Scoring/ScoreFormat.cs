using System.Globalization;

namespace Crossword.Core.Scoring;

/// <summary>
/// Player-facing numbers for scores, chips, mult and deadlines, shared by every frontend. Numbers print in full with
/// thousands separators while they fit a label's digit budget ("1,234,567,890"); past it they shorten to three
/// significant digits and a word ("1.23 trillion"), Disgaea-style — never scientific notation. Always the invariant
/// culture, so saves, logs and screenshots read the same on every machine.
/// </summary>
public static class ScoreFormat
{
    /// <summary>Digits a label shows in full before shortening, when the caller has no tighter budget.</summary>
    public const int DefaultDigits = 12;

    private static readonly (decimal Unit, string Name)[] Units =
    [
        (1_000m, "thousand"),
        (1_000_000m, "million"),
        (1_000_000_000m, "billion"),
        (1_000_000_000_000m, "trillion"),
        (1_000_000_000_000_000m, "quadrillion"),
        (1_000_000_000_000_000_000m, "quintillion"),
    ];

    /// <summary>A whole number: grouped digits up to <paramref name="maxDigits"/> digits, then "1.23 billion".</summary>
    public static string Number(long value, int maxDigits = DefaultDigits)
    {
        if (value == long.MinValue)
            value = long.MinValue + 1;
        string sign = value < 0 ? "-" : "";
        long magnitude = Math.Abs(value);
        if (Digits(magnitude) <= Math.Max(1, maxDigits))
            return sign + magnitude.ToString("N0", CultureInfo.InvariantCulture);
        return sign + Compact(magnitude);
    }

    /// <summary>A number mid count-up (or any fraction): rounded to the nearest whole number, then <see cref="Number(long,int)"/>.</summary>
    public static string Number(decimal value, int maxDigits = DefaultDigits)
    {
        decimal rounded = Math.Round(Math.Clamp(value, long.MinValue, long.MaxValue), MidpointRounding.AwayFromZero);
        return Number((long)rounded, maxDigits);
    }

    /// <summary>
    /// A mult: up to two decimals while small ("2.5", "12.75"), whole and grouped from 1,000 ("1,234"), shortened
    /// like scores past the digit budget.
    /// </summary>
    public static string Mult(decimal value, int maxDigits = DefaultDigits)
    {
        if (Math.Abs(value) < 1000m)
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        return Number(decimal.Truncate(Math.Clamp(value, long.MinValue, long.MaxValue)), maxDigits);
    }

    private static int Digits(long magnitude)
    {
        int digits = 1;
        while (magnitude >= 10)
        {
            magnitude /= 10;
            digits++;
        }
        return digits;
    }

    private static string Compact(long magnitude)
    {
        int i = Units.Length - 1;
        while (i > 0 && magnitude < Units[i].Unit)
            i--;
        decimal quotient = magnitude / Units[i].Unit;
        string text = Significant(quotient);
        if (text == "1000" && i + 1 < Units.Length)
        {
            i++;
            text = "1";
        }
        return $"{text} {Units[i].Name}";
    }

    /// <summary>Three significant digits of a value in [1, 1000), trailing zeros trimmed ("1.23", "12.3", "123", "1000" when it rounds up).</summary>
    private static string Significant(decimal value)
    {
        string format = value >= 100m ? "0" : value >= 10m ? "0.#" : "0.##";
        return Math.Round(value, value >= 100m ? 0 : value >= 10m ? 1 : 2, MidpointRounding.AwayFromZero).ToString(format, CultureInfo.InvariantCulture);
    }
}
