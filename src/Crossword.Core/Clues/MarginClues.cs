using System.Collections.Immutable;
using Crossword.Core.Domain;
using Crossword.Core.Profile;

namespace Crossword.Core.Clues;

/// <summary>Where a margin clue comes from.</summary>
public enum ClueKind
{
    /// <summary>A word on the current board, numbered like a printed crossword, clued by its definition.</summary>
    Board,

    /// <summary>A lifetime record from the player's profile ("from the morgue").</summary>
    Record,

    /// <summary>A default newsroom clue that teaches a mechanic ("editor's notes").</summary>
    Tip,
}

/// <summary>One line in an ACROSS or DOWN margin column. <see cref="Number"/> is set only for board words.</summary>
public sealed record MarginClue(ClueKind Kind, int? Number, string Answer, string Text);

public sealed record ClueSheet(ImmutableArray<MarginClue> Across, ImmutableArray<MarginClue> Down);

/// <summary>
/// Builds the ACROSS / DOWN clue columns printed beside the board. Each column lists the board's words in that
/// direction first (all of them), then fills up to the slot count with lifetime records the player has earned, then
/// with default newsroom tips (never repeating an answer already in the column) — so a fresh profile shows tips, and each tip gives way to a record as stats grow.
/// Records about words go ACROSS; records about runs and mechanics go DOWN. Pure: the UI rebuilds it on every refresh.
/// </summary>
public static class MarginClues
{
    public const string NoDefinition = "valid word — no definition on file";

    public static ClueSheet For(Board board, PlayerStats stats, Func<string, string?> define, int slotsPerColumn = 12)
    {
        var words = BoardWords.Numbered(board);
        return new ClueSheet(
            Column(words, Direction.Across, AcrossRecords(stats), NewsroomClues.Across, define, slotsPerColumn),
            Column(words, Direction.Down, DownRecords(stats), NewsroomClues.Down, define, slotsPerColumn));
    }

    private static ImmutableArray<MarginClue> Column(ImmutableArray<NumberedWord> words, Direction direction,
        IEnumerable<MarginClue> records, ImmutableArray<MarginClue> tips, Func<string, string?> define, int slots)
    {
        var column = words.Where(w => w.Direction == direction)
            .Select(w => new MarginClue(ClueKind.Board, w.Number, w.Text, define(w.Text) is { Length: > 0 } d ? d : NoDefinition))
            .ToList();
        var shown = column.Select(c => c.Answer).ToHashSet(StringComparer.Ordinal);
        foreach (var clue in records.Concat(tips))
        {
            if (column.Count >= slots)
                break;
            if (shown.Add(clue.Answer)) // one clue per answer: the longest word is often also the newest
                column.Add(clue);
        }
        return column.ToImmutableArray();
    }

    /// <summary>Word records, most interesting first; each appears only once its stat exists.</summary>
    public static IEnumerable<MarginClue> AcrossRecords(PlayerStats stats)
    {
        if (stats.LongestWord is { } longest)
            yield return Record(longest, $"Longest word on record ({longest.Length} letters)");
        if (stats.BestPlayWords is { } best)
        {
            string star = best.Split(" + ").MaxBy(w => w.Length)!;
            yield return Record(star, $"Headline word of your record {stats.BestPlayScore:N0}-point play");
        }
        var five = StatsQueries.ByLength(stats).FirstOrDefault(b => b.Length == 5 && !b.OrLonger)?.MostUsed;
        if (five is { IsEmpty: false } fives)
            yield return Record(fives[0].Word, $"Top 5-letter play (used {fives[0].Count}×)");
        if (StatsQueries.Newest(stats, 1) is [var newest])
            yield return Record(newest, "Newest addition to your vocabulary");
        if (stats.Words.Count > 0)
            yield return Record($"{stats.Words.Count:N0} DISTINCT", "Unique dictionary entries on record");
        if (stats.Words.Count > 0)
        {
            var (word, use) = stats.Words.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
            yield return Record(word, $"Your most-used word ({use.Count}×)");
        }
    }

    /// <summary>Run and mechanics records, most interesting first.</summary>
    public static IEnumerable<MarginClue> DownRecords(PlayerStats stats)
    {
        if (stats.RunsStarted > 0)
            yield return Record(Count(stats.RunsStarted, "RUN", "RUNS"), "Editorial shifts logged at the press");
        if (stats.BestWeekReached > 0)
            yield return Record($"WEEK {stats.BestWeekReached}", "Furthest deadline reached");
        if (stats.PlaysRecorded > 0)
            yield return Record(Count(stats.PlaysRecorded, "PLAY", "PLAYS"), "Valid word submissions on record");
        var twos = StatsQueries.ByLength(stats).FirstOrDefault(b => b.Length == 2)?.MostUsed;
        if (twos is { IsEmpty: false } connectors)
            yield return Record(string.Join(" / ", connectors.Take(3).Select(w => w.Word)), "Most frequent two-letter connectors");
        if (stats.TotalIntersections > 0)
            yield return Record(Count(stats.TotalIntersections, "CROSS", "CROSSES"), "Lifetime letter intersections formed");
        if (stats.RunsWon > 0)
            yield return Record(Count(stats.RunsWon, "WIN", "WINS"), "Five-week runs put to bed");
        if (stats.CloseCalls > 0)
            yield return Record(Count(stats.CloseCalls, "CLOSE CALL", "CLOSE CALLS"), "Rounds saved on the final submission");
        if (stats.BossesBeaten.Count > 0)
        {
            var (boss, count) = stats.BossesBeaten.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
            yield return Record(boss.ToUpperInvariant(), $"Boss editor you've beaten most ({count}×)");
        }
        if (stats.FullSpreadRounds > 0)
            yield return Record(Count(stats.FullSpreadRounds, "FULL SPREAD", "FULL SPREADS"), "Rounds whose board used every vowel or ten letters");
    }

    private static MarginClue Record(string answer, string text) => new(ClueKind.Record, null, answer, text);

    private static string Count(long n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";
}

/// <summary>Default newsroom clues: on a fresh profile they fill the margins and teach the game's mechanics.</summary>
public static class NewsroomClues
{
    public static ImmutableArray<MarginClue> Across { get; } =
    [
        Tip("DEADLINE", "The score this edition must reach before the presses roll"),
        Tip("INTERSECT", "Cross an existing word: each new tile in two words adds Mult"),
        Tip("LONGEST", "Your play's longest word sets its base chips and mult"),
        Tip("DOUBLE WORD", "2W and 3W squares multiply a word, but only under new tiles"),
        Tip("OVERKILL", "Finishing far past the deadline pays extra at payday"),
        Tip("INTEREST", "Cash you hold earns interest at payday, up to a cap"),
        Tip("WILD", "A blank that plays as any letter, worth 0 chips"),
        Tip("STYLE GUIDE", "Permanently levels up one word length"),
        Tip("DISCARD", "Trade away tiles you can't use; a few per round"),
        Tip("HINT", "A decent play, never the best; an Answer Key shows that"),
    ];

    public static ImmutableArray<MarginClue> Down { get; } =
    [
        Tip("COPY", "Raw text awaiting the red pen"),
        Tip("TYPESET", "Arranging lead tiles on the day's frame"),
        Tip("DESK ITEMS", "Apply left to right: +Mult before ×Mult"),
        Tip("STATIONERY", "One-shot supplies, used mid-round"),
        Tip("BOLD", "A tile that adds chips to every word it's in"),
        Tip("ITALIC", "A tile that adds mult to every word it's in"),
        Tip("GILDED", "A tile that pays a dollar for every word it's in"),
        Tip("SUNDAY", "Edition day: a boss editor changes the rules"),
        Tip("BAG", "Tiles left to draw this round"),
        Tip("REROLL", "Fresh shop offers, a dollar dearer each time"),
    ];

    private static MarginClue Tip(string answer, string text) => new(ClueKind.Tip, null, answer, text);
}
