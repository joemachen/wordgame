using Crossword.Core.Lexicon;
using Crossword.Core.Profile;
using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Player stats popup (sidebar "Stats" button): lifetime totals, per-length word lists (most and least used) and the
/// newest words, from the saved player profile. Hovering a word shows its definition.
/// </summary>
public partial class Main
{
    private ProfileStore _profile = null!;
    private bool _runEndRecorded;
    private Control _statsOverlay = null!;
    private VBoxContainer _statsBox = null!;

    private Control BuildStatsOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                ToggleStats(); // click outside the panel closes
        };

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 20, radius: 12, border: UiKit.Money, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        _statsBox = UiKit.VBox(8);
        panel.AddChild(_statsBox);
        centre.AddChild(panel);
        return overlay;
    }

    private void ToggleStats()
    {
        _statsOverlay.Visible = !_statsOverlay.Visible;
        if (_statsOverlay.Visible)
            RefreshStats();
    }

    private void RefreshStats()
    {
        UiKit.ClearChildren(_statsBox);
        var stats = _profile.Profile.Stats;

        _statsBox.AddChild(UiKit.MakeLabel($"{_profile.Profile.Name}'s Stats", 26, UiKit.Text));
        string best = stats.BestPlayWords is null ? "—" : $"{stats.BestPlayScore:N0} ({stats.BestPlayWords})";
        var overview = UiKit.MakeLabel(
            $"Runs {stats.RunsStarted}   ·   Wins {stats.RunsWon}   ·   Furthest week {(stats.BestWeekReached == 0 ? "—" : stats.BestWeekReached)}\n"
            + $"Distinct words {stats.Words.Count:N0}   ·   Plays {stats.PlaysRecorded:N0}   ·   Longest word {stats.LongestWord ?? "—"}   ·   Best play {best}\n"
            + $"Intersections {stats.TotalIntersections:N0}   ·   Close calls {stats.CloseCalls:N0}   ·   Full spreads {stats.FullSpreadRounds:N0}"
            + (stats.BossesBeaten.Count == 0 ? "" : $"   ·   Most-beaten boss {stats.BossesBeaten.MaxBy(kv => kv.Value).Key} ({stats.BossesBeaten.Values.Max()}×)"),
            15, UiKit.TextMuted, wrap: true);
        overview.CustomMinimumSize = new Vector2(860, 0);
        _statsBox.AddChild(overview);
        _statsBox.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 22);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var header in new[] { "LENGTH", "WORDS · USES", "MOST USED", "LEAST USED" })
            grid.AddChild(UiKit.MakeLabel(header, 12, UiKit.TextMuted));
        foreach (var bucket in StatsQueries.ByLength(stats))
        {
            grid.AddChild(UiKit.MakeLabel(bucket.Label, 15, UiKit.Text));
            grid.AddChild(UiKit.MakeLabel($"{bucket.DistinctWords:N0} · {bucket.TotalUses:N0}", 15, UiKit.Text));
            grid.AddChild(WordRow(bucket.MostUsed.Select(w => (w.Word, $"×{w.Count}")), UiKit.Good));
            grid.AddChild(WordRow(bucket.LeastUsed.Select(w => (w.Word, $"×{w.Count}")), UiKit.TextMuted));
        }
        _statsBox.AddChild(grid);
        _statsBox.AddChild(new HSeparator());

        _statsBox.AddChild(UiKit.MakeLabel("NEWEST WORDS", 12, UiKit.TextMuted));
        var newest = StatsQueries.Newest(stats);
        _statsBox.AddChild(newest.IsEmpty
            ? UiKit.MakeLabel("Play a word to start your collection.", 15, UiKit.TextMuted)
            : WordRow(newest.Select(w => (w, "")), UiKit.Selected));

        _statsBox.AddChild(UiKit.MakeLabel("Vocabulary grading is coming later. Click anywhere to close.", 13, UiKit.TextMuted));
    }

    /// <summary>A row of words, each showing its definition on hover.</summary>
    private static HBoxContainer WordRow(IEnumerable<(string Word, string Suffix)> words, Color color)
    {
        var row = UiKit.HBox(10);
        foreach (var (word, suffix) in words)
        {
            var label = UiKit.MakeLabel(suffix.Length == 0 ? word : $"{word} {suffix}", 15, color);
            label.MouseFilter = MouseFilterEnum.Pass;
            label.TooltipText = DefinitionLoader.Default.Define(word)?.Summary ?? "valid word — no definition on file";
            row.AddChild(label);
        }
        if (row.GetChildCount() == 0)
            row.AddChild(UiKit.MakeLabel("—", 15, UiKit.TextMuted));
        return row;
    }

    /// <summary>Records the end of the run once, when it is won or lost.</summary>
    private void RecordRunEndIfOver()
    {
        if (_runEndRecorded || _session.Phase is not (RunPhase.Victory or RunPhase.Defeat))
            return;
        _runEndRecorded = true;
        bool won = _session.Phase == RunPhase.Victory;
        int unlockedBefore = StatsQueries.UnlockedPressRun(_profile.Profile.Stats, Run.DeckId);
        int decksBefore = StatsQueries.UnlockedDecks(_profile.Profile.Stats).Length;
        _profile.Update(s => StatsRules.RecordRunEnd(s, won, _session.Week + 1, Run.PressRun, Run.DeckId));
        int unlockedNow = StatsQueries.UnlockedPressRun(_profile.Profile.Stats, Run.DeckId);
        var decksNow = StatsQueries.UnlockedDecks(_profile.Profile.Stats);
        _justUnlockedPressRun = unlockedNow > unlockedBefore ? unlockedNow : null;
        _justUnlockedDeck = decksNow.Length > decksBefore ? decksNow[^1] : null;
    }
}
