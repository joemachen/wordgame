using System.Text;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
using Crossword.Core.Run;
using Crossword.Core.Scoring;

namespace Crossword.Cli;

/// <summary>Plain-text views of Core state. Pure string builders; Program decides where to print.</summary>
public static class ConsoleRenderer
{
    public static string Board(Board board)
    {
        var sb = new StringBuilder("    ");
        for (int c = 0; c < board.Size; c++)
            sb.Append(' ').Append((char)('A' + c)).Append(' ');
        sb.AppendLine();

        for (int r = 0; r < board.Size; r++)
        {
            sb.Append($"{r + 1,3} ");
            for (int c = 0; c < board.Size; c++)
            {
                var p = new Position(r, c);
                sb.Append(board.TileAt(p) is { } tile ? $" {tile}{EnhancementMark(tile.Enhancement)}"
                    : board.IsBlocked(p) ? "###"
                    : PremiumCell(board.PremiumAt(p)));
            }
            sb.AppendLine();
        }

        sb.Append("    2L/3L = double/triple letter   2W/3W = double/triple word");
        if (!board.Blocked.IsEmpty)
            sb.Append("   ### = black square");
        if (board.Cells.Any(t => t is { Enhancement: not TileEnhancement.None }))
            sb.Append("   + bold  / italic  $ gilded");
        return sb.ToString();
    }

    private static string PremiumCell(Premium premium) => premium switch
    {
        Premium.DoubleLetter => "2L ",
        Premium.TripleLetter => "3L ",
        Premium.DoubleWord => "2W ",
        Premium.TripleWord => "3W ",
        _ => " . ",
    };

    private static char EnhancementMark(TileEnhancement enhancement) => enhancement switch
    {
        TileEnhancement.Bold => '+',
        TileEnhancement.Italic => '/',
        TileEnhancement.Gilded => '$',
        _ => ' ',
    };

    private static string TileLabel(Tile tile, ScoringConfig scoring) =>
        tile.Enhancement == TileEnhancement.None
            ? $"{tile}({scoring.ValueOf(tile)})"
            : $"{tile}({scoring.ValueOf(tile)}){tile.Enhancement}";

    public static string Hand(Hand hand, ScoringConfig scoring) =>
        "Hand: " + string.Join("  ", hand.Tiles.Select(t => TileLabel(t, scoring)));

    public static string Header(GameSession session)
    {
        string boss = session.Round.Config.Boss is { } b
            ? $"BOSS: {b.Name} — {b.Description}"
            : $"Sunday boss: {session.WeekBoss.Name} — {session.WeekBoss.Description}";
        string press = (session.Run.DeckId != Decks.StandardId ? $"{Decks.Get(session.Run.DeckId).Name} · " : "")
            + (session.Run.PressRun > PressRuns.Lowest ? $"Press Run {session.Run.PressRun} · " : "");
        return $"{press}Week {session.Week + 1}/{session.Config.WeekTargets.Length} · {session.Kind.Name} · ${session.Run.Money} | {boss}";
    }

    public static string Status(RoundState round) =>
        $"Score {round.Score}/{round.Config.TargetScore} | Submissions {round.SubmissionsLeft} | " +
        $"Discards {round.DiscardsLeft} | Bag {round.Bag.Count}" +
        (round.Config.CensoredLetter is { } censored ? $" | Censored: {censored}" : "") +
        (round.Config.Highlight is { } highlight ? $" | Highlighter x{highlight.Factor} on tile #{highlight.TileId}" : "") +
        (round.Config.Clipping is { } clipping ? $" | Clipping: {clipping.Text}" : "") +
        (round.Config.IllegalWordsAllowed > 0 ? $" | Poetic License x{round.Config.IllegalWordsAllowed}" : "");

    public static string Desk(RunState run, ShopConfig shop, int slots = RunState.MaxDeskSlots) =>
        run.DeskItems.IsEmpty
            ? $"Desk: (empty, {slots} slots)"
            : $"Desk ({run.DeskItems.Length}/{slots}): " +
              string.Join("  ", run.DeskItems.Select((item, i) =>
                  $"[{i + 1}] {item.Name}: {item.Description} (sell ${shop.SellValueOf(item)})"));

    public static string Stationery(RunState run, ShopConfig shop) =>
        $"Stationery ({run.Stationery.Length}/{RunState.MaxStationerySlots}): " +
        string.Join("  ", Enumerable.Range(0, RunState.MaxStationerySlots).Select(i => i < run.Stationery.Length
            ? $"[{i + 1}] {run.Stationery[i].Name}: {run.Stationery[i].Description} (sell ${shop.SellValueOf(run.Stationery[i])})"
            : $"[{i + 1}] (empty)"));

    public static string Deck(RunState run, ScoringConfig scoring)
    {
        var counts = run.Deck.GroupBy(t => t.IsWild ? '?' : t.Letter.Char).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}");
        var enhanced = run.Deck.Where(t => t.Enhancement != TileEnhancement.None).GroupBy(t => t.Enhancement).OrderBy(g => g.Key)
            .Select(g => $"\n  {scoring.Describe(g.Key)}: {string.Join(", ", g.Select(t => t.ToString()))}").ToList();
        return $"Deck ({run.Deck.Length} tiles): {string.Join(" ", counts)}" + string.Concat(enhanced);
    }

    /// <summary>Word tier table with Style Guide levels.</summary>
    public static string Tiers(GameSession session)
    {
        var tiers = session.Scoring.Tiers;
        return "Word tiers: " + string.Join("  ", tiers.Select((t, i) =>
            $"{t.Label(i == tiers.Length - 1)} Lv{session.Run.TierUpgrades.GetValueOrDefault(t.MinLength) + 1} {t.BaseChips}x{t.BaseMult:0.##}"));
    }

    public static string ScoreBreakdown(ScoreContext score)
    {
        var sb = new StringBuilder();
        foreach (var evt in score.Log)
            sb.AppendLine($"  {evt}");
        sb.Append($"  = {score.Chips} chips × {score.Mult:0.##} mult = {score.Total} points");
        if (score.Money > 0)
            sb.Append($"   (+${score.Money})");
        return sb.ToString();
    }

    public static string Paycheck(Payout payout, int moneyAfter, EconomyConfig economy)
    {
        var sb = new StringBuilder("=== Paycheck ===\n");
        sb.AppendLine($"  Column fee            ${payout.Base}");
        if (payout.UnusedSubmissions > 0)
            sb.AppendLine($"  Unused submissions    ${payout.UnusedSubmissions}");
        if (payout.UnusedDiscards > 0)
            sb.AppendLine($"  Unused discards       ${payout.UnusedDiscards}");
        if (payout.Overkill > 0)
            sb.AppendLine($"  Overkill bonus        ${payout.Overkill}");
        if (payout.Interest > 0)
            sb.AppendLine($"  Interest              ${payout.Interest}");
        if (payout.FloorTopUp > 0)
            sb.AppendLine($"  Minimum paycheck      ${payout.FloorTopUp}");
        sb.AppendLine($"  Total ${payout.Total}  →  you have ${moneyAfter}");
        sb.Append($"  {Economy.HowToEarnMore(economy)}");
        return sb.ToString();
    }

    public static string Shop(GameSession session)
    {
        var shop = session.Shop!;
        int next = session.Run.RoundIndex + 1;
        var nextKind = session.Config.KindOf(next);
        string nextBoss = nextKind.IsBoss ? $" — BOSS: {RunRules.BossFor(session.Config, session.Run, session.Config.WeekOf(next)).Name}" : "";
        var sb = new StringBuilder($"=== Shop ===  ${session.Run.Money}   (reroll ${shop.RerollCost})   " +
                                   $"Next: Week {session.Config.WeekOf(next) + 1} {nextKind.Name}, target {RunRules.TargetFor(session.Config, session.Run, next)}{nextBoss}\n");
        for (int i = 0; i < shop.Offers.Length; i++)
        {
            string line = shop.Offers[i] is { } offer ? $"${offer.Price,-3} {offer.Describe(session.Scoring)}" : "(sold)";
            sb.AppendLine($"  [{i + 1}] {line}");
        }
        sb.Append("  buy <n> [LETTERS] · sell <slot> · sellst <slot> · reroll · leave");
        return sb.ToString();
    }
}
