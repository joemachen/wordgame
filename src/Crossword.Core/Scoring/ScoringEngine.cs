using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Rules;

namespace Crossword.Core.Scoring;

/// <summary>
/// Scores a validated play as one pooled Chips × Mult:
/// 1. Tier — longest word sets base Chips and Mult.
/// 2. Words — every formed word adds its letter chips (DL/TL per tile, then DW/TW per word; new tiles only);
///    under Redundant Copy a word already formed this round adds none.
/// 3. Enhancements — each enhanced tile triggers once per formed word containing it (new or existing tiles).
/// 4. Intersections — each new tile in both an Across and a Down word adds Mult.
/// 4b. Theme words — each formed word in the run's theme dictionary adds Mult (The Olde English Folio).
/// 4c. Round bonus — flat Mult from Stationery used this round (Red Ink Bottle).
/// 5. Desk Items — applied in slot order.
/// 6. Total — floor(Chips × Mult).
/// </summary>
public static class ScoringEngine
{
    public static class Sources
    {
        public const string Tier = "tier";
        public const string Word = "word";
        public const string Intersection = "intersection";
        public const string Enhancement = "enhancement";
        public const string Bonus = "bonus";
        public const string Theme = "theme";
    }

    public static ScoreContext Score(PlayAnalysis play, IReadOnlyList<IDeskItem> deskItemsInSlotOrder, ScoringConfig config,
        ScoreEnvironment? environment = null)
    {
        var context = ApplyTier(play, config) with { Env = environment ?? ScoreEnvironment.Empty };
        context = play.Words.Aggregate(context, (ctx, word) => ApplyWord(ctx, word, config));
        context = play.Words.Aggregate(context, (ctx, word) => ApplyEnhancements(ctx, word, config));
        context = ApplyIntersections(context, config);
        if (config.Theme is { } theme)
            context = play.Words.Where(word => theme.Words.Contains(word.Text)).Aggregate(context, (ctx, word) =>
                ctx.AddMult(theme.MultPerWord).Record(Sources.Theme, $"{word.Text} ({theme.Name}): +{theme.MultPerWord} mult"));
        if (config.BonusMult != 0)
            context = context.AddMult(config.BonusMult).Record(Sources.Bonus, $"Red ink: +{config.BonusMult} mult");
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
            letters += config.ValueOf(cell.Tile) * letterMultiplier;
        }
        return (letters * wordMultiplier, wordMultiplier);
    }

    private static ScoreContext ApplyWord(ScoreContext context, FormedWord word, ScoringConfig config)
    {
        if (config.RepeatWordsScoreZero && context.Env.WordsFormed.Contains(word.Text))
            return (context with { WordChips = context.WordChips.Add(0) })
                .Record(Sources.Word, $"{word.Text}: already printed this round, 0 chips");

        var (chips, wordMultiplier) = WordChips(word, context.Play.BoardAfter, config);
        string suffix = wordMultiplier > 1 ? $" (×{wordMultiplier} word)" : string.Empty;
        return (context with { WordChips = context.WordChips.Add(chips) }).AddChips(chips)
            .Record(Sources.Word, $"{word.Text}: +{chips} chips{suffix}");
    }

    private static ScoreContext ApplyEnhancements(ScoreContext context, FormedWord word, ScoringConfig config)
    {
        foreach (var cell in word.Cells)
        {
            var tile = cell.Tile;
            context = tile.Enhancement switch
            {
                TileEnhancement.Bold => context.AddChips(config.BoldChips)
                    .Record(Sources.Enhancement, $"Bold {tile} in {word.Text}: +{config.BoldChips} chips"),
                TileEnhancement.Italic => context.AddMult(config.ItalicMult)
                    .Record(Sources.Enhancement, $"Italic {tile} in {word.Text}: +{config.ItalicMult} mult"),
                TileEnhancement.Gilded => context.AddMoney(config.GildedMoney)
                    .Record(Sources.Enhancement, $"Gilded {tile} in {word.Text}: +${config.GildedMoney}"),
                _ => context,
            };
        }
        return context;
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
