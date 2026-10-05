using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;

namespace Crossword.Core.Analysis;

public sealed record SimulatedPlay(int Submission, long Score, int LongestWord, int WordsFormed, int Intersections, int TilesUsed);

public sealed record SimulatedRound(ulong Seed, ImmutableArray<SimulatedPlay> Plays, long FinalScore, int DiscardsUsed, bool Deadlocked);

/// <summary>
/// Balance tooling: plays rounds automatically, discarding the whole hand when nothing is playable.
/// <c>skill</c> 1.0 = greedy (always the best play, an upper bound on human play); lower values pick the play at
/// that percentile of the ranking (0.9 = the play better than 90% of legal plays), a rough proxy for weaker players.
/// </summary>
public static class RoundSimulator
{
    /// <summary>Plays every submission of one round (target ignored, so the full score potential is measured).</summary>
    public static SimulatedRound PlayRound(
        ulong seed, RoundConfig config, IWordGraph lexicon, IReadOnlyList<IDeskItem> deskItems, ScoringConfig scoring,
        double skill = 1.0)
    {
        if (skill is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(skill), skill, "Skill must be in (0, 1].");

        var uncapped = config with { TargetScore = long.MaxValue };
        var (round, _) = RoundRules.Start(RunState.New(seed), uncapped, lexicon);
        var plays = ImmutableArray.CreateBuilder<SimulatedPlay>();
        int submission = 0;

        while (round.Status == RoundStatus.InProgress)
        {
            var ranked = MoveRanker.Rank(round.Board, round.Hand, lexicon, deskItems, scoring);
            if (ranked.Count == 0)
            {
                var discarded = RoundRules.Discard(round, round.Hand.Tiles.Select(t => t.Id).ToArray(), lexicon);
                if (!discarded.IsOk)
                    break;
                round = discarded.Value;
                continue;
            }

            var best = ranked[(int)((1 - skill) * (ranked.Count - 1))];
            var outcome = RoundRules.Submit(round, best.Play.Placed, lexicon, deskItems, scoring).Value;
            submission++;
            plays.Add(new SimulatedPlay(
                submission,
                outcome.Score.Total,
                best.Play.LongestWord.Length,
                best.Play.Words.Length,
                best.Play.Intersections.Length,
                best.Play.Placed.Length));
            round = outcome.State;
        }

        return new SimulatedRound(seed, plays.ToImmutable(), round.Score, config.Discards - round.DiscardsLeft, round.Deadlocked);
    }
}
