using Crossword.Core.Clues;
using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// ACROSS / DOWN clue columns printed beside the board (<see cref="MarginClues"/>): the board's words, numbered like a
/// crossword and clued by their definitions, then lifetime records ("From the morgue"), then newsroom tips
/// ("Editor's notes"). Rebuilt on every refresh during a round, so new words and records appear immediately.
/// </summary>
public partial class Main
{
    /// <summary>
    /// Off for now (user's call, 2026-10-05): the columns were distracting; they come back with the visual overhaul.
    /// When off, the columns and tile clue numbers are hidden; the clue engine and stats keep running in Core.
    /// </summary>
    private static readonly bool ShowClueColumns = false;

    private const int ClueSlots = 12;
    private const float ClueColumnWidth = 230;

    private static readonly Color ClueInk = new("2b2620");
    private static readonly Color ClueMuted = new("6b6253");

    private VBoxContainer _acrossBox = null!;
    private VBoxContainer _downBox = null!;

    /// <summary>Clue numbers by board cell (cells that start a word), for the small number on board tiles.</summary>
    private Dictionary<GridPos, int> _clueNumbers = new();

    private Control BuildClueColumn(out VBoxContainer box)
    {
        var panel = UiKit.MakePanel(UiKit.Newsprint, padding: 10, radius: 4, border: new Color("cfc4a8"), borderWidth: 1);
        panel.Visible = ShowClueColumns;
        panel.CustomMinimumSize = new Vector2(ClueColumnWidth, 0);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        box = UiKit.VBox(5);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(box);
        return panel;
    }

    private void RefreshClues()
    {
        if (!ShowClueColumns)
            return;
        var sheet = MarginClues.For(Round.Board, _profile.Profile.Stats, w => Define(w)?.Summary, ClueSlots);
        FillClueColumn(_acrossBox, "ACROSS", sheet.Across);
        FillClueColumn(_downBox, "DOWN", sheet.Down);
    }

    private static void FillClueColumn(VBoxContainer box, string title, IReadOnlyList<MarginClue> clues)
    {
        UiKit.ClearChildren(box);
        var heading = UiKit.MakeLabel(title, 17, ClueInk);
        heading.Name = "Heading";
        box.AddChild(heading);
        box.AddChild(new HSeparator());

        ClueKind? section = ClueKind.Board;
        foreach (var clue in clues)
        {
            if (clue.Kind != section)
            {
                section = clue.Kind;
                if (clue.Kind != ClueKind.Board)
                    box.AddChild(UiKit.MakeLabel(clue.Kind == ClueKind.Record ? "FROM THE MORGUE" : "EDITOR'S NOTES", 11, ClueMuted));
            }
            var line = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(ClueColumnWidth - 28, 0),
            };
            line.AddThemeFontSizeOverride("normal_font_size", 13);
            line.AddThemeFontSizeOverride("bold_font_size", 13);
            line.AddThemeColorOverride("default_color", ClueInk);
            string number = clue.Number is int n ? $"[b]{n}[/b]  " : "";
            line.Text = $"{number}[b]{Escape(clue.Answer)}[/b] — {Escape(clue.Text)}";
            box.AddChild(line);
        }
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    /// <summary>All clue texts currently shown in a column (self-test hook).</summary>
    private static string ClueText(VBoxContainer box) =>
        string.Join("\n", box.GetChildren().Select(c => c switch
        {
            RichTextLabel r => r.GetParsedText(),
            Label l => l.Text,
            _ => "",
        }));

    /// <summary>Records a won round once (close calls, bosses, full spreads).</summary>
    private void RecordRoundWonIfDone()
    {
        if (Round.Status != Crossword.Core.Domain.RoundStatus.Won || _roundWonRecorded == Run.RoundIndex)
            return;
        _roundWonRecorded = Run.RoundIndex;
        _profile.Update(s => StatsRules.RecordRoundWon(s, Round));
    }

    private int _roundWonRecorded = -1;
}
