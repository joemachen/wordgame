using System.Text;
using Crossword.Core.Domain;
using Crossword.Core.Effects;
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
                sb.Append(board.TileAt(p) is { } tile ? $" {tile} "
                    : board.IsBlocked(p) ? "###"
                    : PremiumCell(board.PremiumAt(p)));
            }
            sb.AppendLine();
        }

        sb.Append("    2L/3L = double/triple letter   2W/3W = double/triple word");
        if (!board.Blocked.IsEmpty)
            sb.Append("   ### = black square");
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

    public static string Hand(Hand hand, ScoringConfig scoring) =>
        "Hand: " + string.Join("  ", hand.Tiles.Select(t => $"{t}({scoring.ValueOf(t.Letter)})"));

    public static string Status(RunState run, RoundState round) =>
        $"Round {run.RoundIndex + 1} | Score {round.Score}/{round.Config.TargetScore} | " +
        $"Submissions {round.SubmissionsLeft} | Discards {round.DiscardsLeft} | Bag {round.Bag.Count}";

    public static string Desk(RunState run) =>
        run.DeskItems.IsEmpty
            ? $"Desk: (empty, {RunState.MaxDeskSlots} slots)"
            : $"Desk ({run.DeskItems.Length}/{RunState.MaxDeskSlots}): " +
              string.Join("  ", run.DeskItems.Select((item, i) => $"[{i + 1}] {item.Name}: {item.Description}"));

    public static string ScoreBreakdown(ScoreContext score)
    {
        var sb = new StringBuilder();
        foreach (var evt in score.Log)
            sb.AppendLine($"  {evt}");
        sb.Append($"  = {score.Chips} chips × {score.Mult:0.##} mult = {score.Total} points");
        return sb.ToString();
    }
}
