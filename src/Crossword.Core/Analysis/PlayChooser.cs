namespace Crossword.Core.Analysis;

/// <summary>How a simulated player turns a skill value into a choice from the ranked legal plays.</summary>
public enum SkillModel
{
    /// <summary>The play at that percentile of the ranking (0.9 = better than 90% of legal plays).</summary>
    Percentile,

    /// <summary>The best play scoring at most that fraction of the best play (0.75 = finds a play worth ≤75% of the best).</summary>
    ScoreFraction,
}

/// <summary>Picks the simulated player's play from a best-first ranking; skill 1.0 is always the best play.</summary>
public static class PlayChooser
{
    public static RankedPlay Choose(IReadOnlyList<RankedPlay> ranked, double skill, SkillModel model = SkillModel.Percentile)
    {
        if (ranked.Count == 0)
            throw new ArgumentException("No plays to choose from.", nameof(ranked));
        if (skill is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(skill), skill, "Skill must be in (0, 1].");

        if (model == SkillModel.Percentile)
            return ranked[(int)((1 - skill) * (ranked.Count - 1))];

        long cap = (long)Math.Floor(ranked[0].Score.Total * skill);
        foreach (var play in ranked)
            if (play.Score.Total <= cap)
                return play;
        return ranked[^1];
    }
}
