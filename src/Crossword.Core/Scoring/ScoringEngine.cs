using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;

namespace Crossword.Core.Scoring;

/// <summary>
/// Scores a validated play as one pooled Chips × Mult:
/// 1. Tier — longest word sets base Chips and Mult.
/// 2. Words — every formed word adds its letter chips (DL/TL per tile, then DW/TW per word; new tiles only).
/// 3. Intersections — each new tile in both an Across and a Down word adds Mult.
/// 4. Desk Items — applied in slot order.
/// 5. Total — floor(Chips × Mult).
/// </summary>
public static class ScoringEngine
{
    public static class Sources
    {
        public const string Tier = "tier";
        public const string Word = "word";
        public const string Intersection = "intersection";
    }

    public static ScoreContext Score(PlayAnalysis play, IReadOnlyList<IDeskItem> deskItemsInSlotOrder, ScoringConfig config)
    {
        var context = ApplyTier(play, config);
        context = play.Words.Aggregate(context, (ctx, word) => ApplyWord(ctx, word, config));
        context = ApplyIntersections(context, config);
        return EffectPipeline.Apply(deskItemsInSlotOrder, context);
    }

    private static ScoreContext ApplyTier(PlayAnalysis play, ScoringConfig config)
    {
        int length = play.LongestWord.Length;
        var tier = config.TierFor(length);
        return ScoreContext.Start(play, tier.BaseChips, tier.BaseMult)
            .Record(Sources.Tier, $"{length}-letter word ({play.LongestWord.Text}): {tier.BaseChips} chips × {tier.BaseMult} mult");
    }

    public static (long Chips, int WordMultiplier) WordChips(FormedWord word, Board board, ScoringConfig config)
    {
        long letters = 0;
        int wordMultiplier = 1;
        foreach (var cell in word.Cells)
        {
            var premium = cell.IsNew ? board.PremiumAt(cell.Position) : Premium.None;
            int letterMultiplier = premium switch
            {
                Premium.DoubleLetter => 2,
                Premium.TripleLetter => 3,
                _ => 1,
            };
            wordMultiplier *= premium switch
            {
                Premium.DoubleWord => 2,
                Premium.TripleWord => 3,
                _ => 1,
            };
            letters += config.ValueOf(cell.Tile.Letter) * letterMultiplier;
        }
        return (letters * wordMultiplier, wordMultiplier);
    }

    private static ScoreContext ApplyWord(ScoreContext context, FormedWord word, ScoringConfig config)
    {
        var (chips, wordMultiplier) = WordChips(word, context.Play.BoardAfter, config);
        string suffix = wordMultiplier > 1 ? $" (×{wordMultiplier} word)" : string.Empty;
        return context.AddChips(chips).Record(Sources.Word, $"{word.Text}: +{chips} chips{suffix}");
    }

    private static ScoreContext ApplyIntersections(ScoreContext context, ScoringConfig config)
    {
        int count = context.Play.Intersections.Length;
        if (count == 0)
            return context;

        decimal bonus = count * config.IntersectionMult;
        return context.AddMult(bonus).Record(Sources.Intersection, $"{count} intersection(s): +{bonus} mult");
    }
}
