namespace Crossword.Core.Scoring;

/// <summary>
/// Saturating arithmetic for scores, chips, mult and deadlines. Scores are meant to compound into the billions and
/// beyond, so nothing here ever throws or wraps: every result is clamped to ±<see cref="MaxScore"/> (chips, scores,
/// deadlines) or ±<see cref="MaxMult"/> (mult). The caps are numeric safety limits, not balance numbers — a play that
/// reaches them simply scores the cap.
/// </summary>
public static class ScoreMath
{
    /// <summary>The largest score, chip count or deadline: one quintillion (10^18), well inside <see cref="long"/>.</summary>
    public const long MaxScore = 1_000_000_000_000_000_000;

    /// <summary>The largest mult, so chips × mult never leaves <see cref="decimal"/>'s range before it is capped.</summary>
    public const decimal MaxMult = 1_000_000_000_000_000_000m;

    /// <summary>floor(chips × mult), never negative, capped at <see cref="MaxScore"/>.</summary>
    public static long Total(long chips, decimal mult)
    {
        if (chips <= 0 || mult <= 0)
            return 0;
        decimal m = Math.Min(mult, MaxMult);
        if (m >= 1 && chips >= MaxScore / m)
            return MaxScore;
        return Math.Min(MaxScore, (long)decimal.Floor(chips * m));
    }

    /// <summary>a + b, clamped to ±<see cref="MaxScore"/>.</summary>
    public static long Add(long a, long b) => Clamp(Clamp(a) + Clamp(b));

    /// <summary>floor(value × factor) for a non-negative factor, clamped to ±<see cref="MaxScore"/>.</summary>
    public static long Scale(long value, decimal factor)
    {
        if (value == 0 || factor <= 0)
            return 0;
        long v = Clamp(value);
        decimal f = Math.Min(factor, MaxMult);
        if (f >= 1 && Math.Abs(v) >= MaxScore / f)
            return v > 0 ? MaxScore : -MaxScore;
        return Clamp((long)decimal.Floor(v * f));
    }

    /// <summary>a + b for mult, clamped to ±<see cref="MaxMult"/>.</summary>
    public static decimal AddMult(decimal a, decimal b) => ClampMult(ClampMult(a) + ClampMult(b));

    /// <summary>mult × factor for a non-negative factor, clamped to ±<see cref="MaxMult"/>.</summary>
    public static decimal MultiplyMult(decimal mult, decimal factor)
    {
        if (mult == 0 || factor <= 0)
            return 0;
        decimal m = ClampMult(mult);
        decimal f = Math.Min(factor, MaxMult);
        if (f >= 1 && Math.Abs(m) >= MaxMult / f)
            return m > 0 ? MaxMult : -MaxMult;
        return ClampMult(m * f);
    }

    public static long Clamp(long value) => Math.Clamp(value, -MaxScore, MaxScore);

    public static decimal ClampMult(decimal value) => Math.Clamp(value, -MaxMult, MaxMult);
}
