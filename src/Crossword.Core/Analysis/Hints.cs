namespace Crossword.Core.Analysis;

/// <summary>
/// Tuning for the free in-round hint. It shows a decent play, never a near-optimal one: the play at
/// <see cref="Percentile"/> of the ranking, or the best play worth at most <see cref="MaxFractionOfBest"/> of the
/// best score, whichever scores lower. The best play itself is the Answer Key's job.
/// </summary>
public sealed record HintConfig(double Percentile = 0.9, double MaxFractionOfBest = 0.6)
{
    public static HintConfig Default { get; } = new();
}

/// <summary>Picks hint plays from a best-first ranking (<see cref="MoveRanker.Rank"/>); null when there is no legal play.</summary>
public static class Hints
{
    /// <summary>The free hint: enough to get unstuck, never the play a strong player would find.</summary>
    public static RankedPlay? Decent(IReadOnlyList<RankedPlay> ranked, HintConfig? config = null)
    {
        if (ranked.Count == 0)
            return null;
        config ??= HintConfig.Default;
        var byRank = PlayChooser.Choose(ranked, config.Percentile, SkillModel.Percentile);
        var byScore = PlayChooser.Choose(ranked, config.MaxFractionOfBest, SkillModel.ScoreFraction);
        return byScore.Score.Total < byRank.Score.Total ? byScore : byRank;
    }

    /// <summary>The best legal play (Answer Key, dev hint).</summary>
    public static RankedPlay? Best(IReadOnlyList<RankedPlay> ranked) => ranked.Count == 0 ? null : ranked[0];
}
