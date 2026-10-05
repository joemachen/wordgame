using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Lexicon;
using Crossword.Core.Rules;
using Crossword.Core.Scoring;

namespace Crossword.Core.Analysis;

public sealed record RankedPlay(PlayAnalysis Play, ScoreContext Score);

public static class MoveRanker
{
    /// <summary>All legal plays scored with the given Desk Items, best first (ties broken by fewer tiles used).</summary>
    public static IReadOnlyList<RankedPlay> Rank(
        Board board, Hand hand, IWordGraph lexicon, IReadOnlyList<IDeskItem> deskItems, ScoringConfig scoring) =>
        MoveGenerator.LegalPlays(board, hand, lexicon)
            .Select(play => new RankedPlay(play, ScoringEngine.Score(play, deskItems, scoring)))
            .OrderByDescending(r => r.Score.Total)
            .ThenBy(r => r.Play.Placed.Length)
            .ToList();
}
