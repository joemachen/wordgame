using System.Collections.Immutable;

namespace Crossword.Core.Run;

/// <summary>Where a round sits in its week, for progress displays.</summary>
/// <param name="Week">Zero-based week (≥ <see cref="WeekCount"/> in endless mode).</param>
/// <param name="Day">Zero-based day within the week.</param>
/// <param name="PuzzlesUntilBoss">Rounds still to play before this week's boss round (0 on the boss round).</param>
public sealed record WeekProgress(int Week, int WeekCount, int Day, ImmutableArray<RoundKind> Days, int BossDay, int PuzzlesUntilBoss)
{
    public bool Endless => Week >= WeekCount;

    public bool IsBossDay => Day == BossDay;
}

public static class RunProgress
{
    public static WeekProgress For(RunConfig config, int roundIndex)
    {
        int day = roundIndex % config.RoundsPerWeek;
        int bossDay = config.Days.IndexOf(config.Days.FirstOrDefault(d => d.IsBoss) ?? config.Days[^1]);
        int until = bossDay >= day ? bossDay - day : config.RoundsPerWeek - day + bossDay;
        return new WeekProgress(config.WeekOf(roundIndex), config.WeekTargets.Length, day, config.Days, bossDay, until);
    }
}
