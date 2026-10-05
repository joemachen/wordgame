using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Run Info popup (Tab): every word tier with its Style Guide, level and current chips × mult, highlighting the guides
/// bought this run and the tier the pending play would score at.
/// </summary>
public partial class Main
{
    private Control _styleGuidesOverlay = null!;
    private VBoxContainer _styleGuidesBox = null!;

    // Tier (min length) the pending play scores at, set by UpdatePreview; null without a valid preview.
    private int? _previewTier;

    // Tier row currently highlighted as "this play" (read by the self-test).
    private int? _highlightedTier;

    private Control BuildStyleGuidesOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                ToggleStyleGuides(); // click outside the panel closes
        };

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 20, radius: 12, border: UiKit.Chips, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        _styleGuidesBox = UiKit.VBox(8);
        panel.AddChild(_styleGuidesBox);
        centre.AddChild(panel);
        return overlay;
    }

    private void ToggleStyleGuides()
    {
        _styleGuidesOverlay.Visible = !_styleGuidesOverlay.Visible;
        if (_styleGuidesOverlay.Visible)
            RefreshStyleGuides();
    }

    private void RefreshStyleGuides()
    {
        UiKit.ClearChildren(_styleGuidesBox);
        _highlightedTier = null;

        _styleGuidesBox.AddChild(UiKit.MakeLabel("Style Guides", 26, UiKit.Text));
        var intro = UiKit.MakeLabel(
            "The longest word in a play sets its base chips × mult. Each Style Guide levels one tier up for the rest of the run.",
            14, UiKit.TextMuted, wrap: true);
        intro.CustomMinimumSize = new Vector2(760, 0);
        _styleGuidesBox.AddChild(intro);

        bool inRound = _session.Phase == RunPhase.InRound;
        var scoring = inRound ? RoundScoring : _session.Scoring;
        var tiers = scoring.Tiers;
        for (int i = 0; i < tiers.Length; i++)
        {
            var tier = tiers[i];
            bool thisPlay = inRound && _pending.Count > 0 && _previewTier == tier.MinLength;
            if (thisPlay)
                _highlightedTier = tier.MinLength;
            _styleGuidesBox.AddChild(StyleGuideRow(tier, tier.Label(i == tiers.Length - 1), thisPlay));
        }

        _styleGuidesBox.AddChild(UiKit.MakeLabel("Tab / Esc to close", 13, UiKit.TextMuted, HorizontalAlignment.Right));
    }

    private Control StyleGuideRow(WordTier tier, string label, bool thisPlay)
    {
        int bought = Run.TierUpgrades.GetValueOrDefault(tier.MinLength);
        var row = UiKit.MakePanel(bought > 0 ? UiKit.PanelRaised : UiKit.Background, padding: 10, radius: 8,
            border: thisPlay ? UiKit.Selected : UiKit.PanelBorder, borderWidth: thisPlay ? 2 : 1);
        var cells = UiKit.HBox(12);
        row.AddChild(cells);

        cells.AddChild(Cell(label, 110, 16, UiKit.Text));
        var name = UiKit.VBox(0);
        name.CustomMinimumSize = new Vector2(290, 0);
        name.AddChild(UiKit.MakeLabel(StyleGuideNames.For(tier.MinLength, label), 17, bought > 0 ? UiKit.Chips.Lightened(0.25f) : UiKit.TextMuted));
        name.AddChild(UiKit.MakeLabel(bought > 0 ? $"bought ×{bought}" : "not owned", 12, bought > 0 ? UiKit.Text : UiKit.TextMuted));
        cells.AddChild(name);
        cells.AddChild(Cell($"Lv {bought + 1}", 60, 16, bought > 0 ? UiKit.Money : UiKit.TextMuted));
        cells.AddChild(Cell($"{tier.BaseChips} × {tier.BaseMult:0.##}", 110, 18, UiKit.Text));
        cells.AddChild(Cell($"next: +{tier.LevelChips} chips, +{tier.LevelMult:0.##} mult", 190, 13, UiKit.TextMuted));
        if (thisPlay)
            cells.AddChild(Cell("this play", 80, 13, UiKit.Selected));
        return row;
    }

    private static Label Cell(string text, float width, int size, Color color)
    {
        var label = UiKit.MakeLabel(text, size, color);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return label;
    }
}
