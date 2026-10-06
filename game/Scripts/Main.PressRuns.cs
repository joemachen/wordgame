using Crossword.Core.Profile;
using Crossword.Core.Run;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Press Run picker: starting a new run asks which difficulty level to play once a second level is unlocked (a win at
/// level N unlocks N + 1). Locked levels are shown greyed so the ladder is visible.
/// </summary>
public partial class Main
{
    private Control _pressRunOverlay = null!;
    private VBoxContainer _pressRunBox = null!;

    // Set when the run that just ended unlocked a new Press Run (shown on the victory screen).
    private int? _justUnlockedPressRun;

    private Control BuildPressRunOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                overlay.Visible = false; // click outside the panel closes
        };

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 20, radius: 12, border: UiKit.Selected, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        _pressRunBox = UiKit.VBox(8);
        panel.AddChild(_pressRunBox);
        centre.AddChild(panel);
        return overlay;
    }

    /// <summary>Starts a new run, first asking for the Press Run when more than one is unlocked.</summary>
    private void ChooseNewRun()
    {
        int unlocked = StatsQueries.UnlockedPressRun(_profile.Profile.Stats);
        if (unlocked == PressRuns.Lowest)
        {
            NewRun((ulong)Time.GetTicksUsec());
            return;
        }
        RefreshPressRuns(unlocked);
        _pressRunOverlay.Visible = true;
    }

    private void RefreshPressRuns(int unlocked)
    {
        UiKit.ClearChildren(_pressRunBox);
        _pressRunBox.AddChild(UiKit.MakeLabel("Choose a Press Run", 26, UiKit.Text));
        var intro = UiKit.MakeLabel("Each Press Run adds its rule to every rule above it. Win a run to unlock the next one.",
            14, UiKit.TextMuted, wrap: true);
        intro.CustomMinimumSize = new Vector2(640, 0);
        _pressRunBox.AddChild(intro);

        foreach (var press in PressRuns.All)
            _pressRunBox.AddChild(PressRunRow(press, press.Level <= unlocked));

        _pressRunBox.AddChild(UiKit.MakeLabel("Esc to close", 13, UiKit.TextMuted, HorizontalAlignment.Right));
    }

    private Control PressRunRow(PressRun press, bool open)
    {
        var color = new Color(press.Color);
        var row = UiKit.MakeButton("", open ? UiKit.PanelRaised : UiKit.Background, 16);
        row.Name = $"PressRun{press.Level}";
        row.Disabled = !open;
        row.CustomMinimumSize = new Vector2(640, 52);
        row.AddThemeStyleboxOverride("normal", UiKit.Box(open ? UiKit.PanelRaised : UiKit.Background, 8, open ? color : UiKit.PanelBorder, 2, 8));
        row.AddThemeStyleboxOverride("hover", UiKit.Box(UiKit.PanelRaised.Lightened(0.08f), 8, color, 3, 8));
        row.AddThemeStyleboxOverride("disabled", UiKit.Box(UiKit.Background, 8, UiKit.PanelBorder, 1, 8));
        if (open)
            row.Pressed += () => PickPressRun(press.Level);

        var cells = UiKit.HBox(12);
        cells.SetAnchorsPreset(LayoutPreset.FullRect);
        cells.OffsetLeft = 12;
        cells.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(cells);

        var chip = new Panel { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", UiKit.Box(open ? color : UiKit.PanelBorder, 7, padding: 0));
        cells.AddChild(chip);
        cells.AddChild(Cell($"{press.Level}", 24, 18, open ? UiKit.Text : UiKit.TextMuted));
        cells.AddChild(Cell(press.Name, 170, 18, open ? color.Lightened(0.2f) : UiKit.TextMuted));
        cells.AddChild(Cell(open ? press.Adds : $"Win Press Run {press.Level - 1} to unlock.", 400, 14, open ? UiKit.Text : UiKit.TextMuted));
        foreach (var child in cells.GetChildren().OfType<Control>())
            child.MouseFilter = MouseFilterEnum.Ignore;
        return row;
    }

    private void PickPressRun(int level)
    {
        _pressRunOverlay.Visible = false;
        NewRun((ulong)Time.GetTicksUsec(), pressRun: level);
    }

    /// <summary>The run's Press Run for the sidebar footer and end screen, e.g. "Press Run 3 · Late Edition".</summary>
    private string PressRunText() => $"Press Run {Run.PressRun} · {PressRuns.Get(Run.PressRun).Name}";
}
