using Crossword.Core.Run;
using Crossword.Core.Scoring;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Style Guides drawer: a panel that slides in from the right edge (Tab, the GUIDES bookmark on the edge, or the
/// sidebar button) listing every word tier with its Style Guide, level and current chips × mult, highlighting the
/// guides bought this run and the tier the pending play would score at. The game stays visible behind it; a click
/// outside, Tab, Esc or the bookmark closes it.
/// </summary>
public partial class Main
{
    private const float GuidesDrawerWidth = 420;
    private const float GuidesHandleDrop = 130; // below the vertical centre, clear of the shop's Next round button

    private Control _styleGuidesOverlay = null!;
    private PanelContainer _styleGuidesPanel = null!;
    private VBoxContainer _styleGuidesBox = null!;
    private Button _guidesHandle = null!;

    // The drawer's logical state: Visible lags it while the close slide plays.
    private bool _styleGuidesOpen;

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
                CloseStyleGuides(); // click outside the drawer closes
        };

        _styleGuidesPanel = UiKit.MakePanel(UiKit.Panel, padding: 18, radius: 0, border: UiKit.Chips, borderWidth: 2);
        _styleGuidesPanel.MouseFilter = MouseFilterEnum.Stop;
        _styleGuidesPanel.SetAnchorsPreset(LayoutPreset.RightWide);
        _styleGuidesPanel.OffsetLeft = -GuidesDrawerWidth;
        _styleGuidesPanel.OffsetRight = 0;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _styleGuidesBox = UiKit.VBox(8);
        _styleGuidesBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_styleGuidesBox);
        _styleGuidesPanel.AddChild(scroll);
        overlay.AddChild(_styleGuidesPanel);
        return overlay;
    }

    /// <summary>The bookmark on the right edge that opens and closes the drawer; hidden behind the title menu.</summary>
    private Button BuildGuidesHandle()
    {
        var handle = new Button
        {
            Text = "G\nU\nI\nD\nE\nS",
            TooltipText = "Style Guides: every word tier's level and chips × mult (Tab)",
            FocusMode = FocusModeEnum.None, // a focused control would turn Tab into focus navigation
        };
        handle.AddThemeFontSizeOverride("font_size", UiKit.FontSize(13));
        handle.AddThemeColorOverride("font_color", UiKit.Text);
        var box = UiKit.Box(UiKit.Chips.Darkened(0.35f), radius: 0, border: UiKit.Chips, borderWidth: 1, padding: 4);
        box.CornerRadiusTopLeft = box.CornerRadiusBottomLeft = 8;
        handle.AddThemeStyleboxOverride("normal", box);
        handle.AddThemeStyleboxOverride("hover", UiKit.Box(UiKit.Chips.Darkened(0.15f), radius: 0, border: UiKit.Chips, borderWidth: 1, padding: 4));
        handle.AddThemeStyleboxOverride("pressed", box);
        handle.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        handle.CustomMinimumSize = new Vector2(26, 128);
        handle.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        handle.OffsetTop += GuidesHandleDrop;
        handle.OffsetBottom += GuidesHandleDrop;
        handle.Pressed += ToggleStyleGuides;
        return handle;
    }

    private void ToggleStyleGuides()
    {
        if (_styleGuidesOpen)
            CloseStyleGuides();
        else
            OpenStyleGuides();
    }

    /// <param name="instant">Skip the slide (screenshots); reduced motion always does.</param>
    private void OpenStyleGuides(bool instant = false)
    {
        if (_styleGuidesOpen)
            return;
        _styleGuidesOpen = true;
        _styleGuidesOverlay.Visible = true;
        _styleGuidesOverlay.MouseFilter = MouseFilterEnum.Stop;
        RefreshStyleGuides();
        SlideDrawer(open: true, instant);
    }

    private void CloseStyleGuides()
    {
        if (!_styleGuidesOpen)
            return;
        _styleGuidesOpen = false;
        _styleGuidesOverlay.MouseFilter = MouseFilterEnum.Ignore; // the game is usable again at once
        SlideDrawer(open: false);
    }

    /// <summary>Slides the panel and the bookmark together; instant with reduced motion.</summary>
    private void SlideDrawer(bool open, bool instant = false)
    {
        float edge = _styleGuidesOverlay.Size.X;
        float panelX = open ? edge - GuidesDrawerWidth : edge;
        float handleX = open ? edge - GuidesDrawerWidth - _guidesHandle.Size.X : edge - _guidesHandle.Size.X;
        if (Juice.ReducedMotion || instant)
        {
            _styleGuidesPanel.Position = new Vector2(panelX, _styleGuidesPanel.Position.Y);
            _guidesHandle.Position = new Vector2(handleX, _guidesHandle.Position.Y);
            if (!open)
                _styleGuidesOverlay.Visible = false;
            return;
        }
        if (open)
            _styleGuidesPanel.Position = new Vector2(edge, _styleGuidesPanel.Position.Y);
        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_styleGuidesPanel, "position:x", panelX, Juice.DrawerSeconds);
        tween.TweenProperty(_guidesHandle, "position:x", handleX, Juice.DrawerSeconds);
        if (!open)
            tween.Chain().TweenCallback(Callable.From(() =>
            {
                if (!_styleGuidesOpen)
                    _styleGuidesOverlay.Visible = false;
            }));
    }

    private void RefreshStyleGuides()
    {
        UiKit.ClearChildren(_styleGuidesBox);
        _highlightedTier = null;

        _styleGuidesBox.AddChild(UiKit.MakeLabel("Style Guides", 24, UiKit.Text));
        var intro = UiKit.MakeLabel(
            "The longest word in a play sets its base chips × mult. Each Style Guide levels one tier up for the rest of the run.",
            13, UiKit.TextMuted, wrap: true);
        intro.CustomMinimumSize = new Vector2(GuidesDrawerWidth - 48, 0);
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

        _styleGuidesBox.AddChild(UiKit.MakeLabel("Tab / Esc / click outside to close", 12, UiKit.TextMuted, HorizontalAlignment.Right));
    }

    /// <summary>A compact two-line row: tier · guide name · level, then chips × mult · next upgrade · owned / this play.</summary>
    private Control StyleGuideRow(WordTier tier, string label, bool thisPlay)
    {
        int bought = Run.TierUpgrades.GetValueOrDefault(tier.MinLength);
        var row = UiKit.MakePanel(bought > 0 ? UiKit.PanelRaised : UiKit.Background, padding: 8, radius: 8,
            border: thisPlay ? UiKit.Selected : UiKit.PanelBorder, borderWidth: thisPlay ? 2 : 1);
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var lines = UiKit.VBox(2);
        row.AddChild(lines);

        var top = UiKit.HBox(10);
        top.AddChild(Cell(label, 80, 15, UiKit.Text));
        var name = UiKit.MakeLabel(StyleGuideNames.For(tier.MinLength, label), 15, bought > 0 ? UiKit.Chips.Lightened(0.25f) : UiKit.TextMuted);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(name);
        top.AddChild(Cell($"Lv {bought + 1}", 46, 15, bought > 0 ? UiKit.Money : UiKit.TextMuted, HorizontalAlignment.Right));
        lines.AddChild(top);

        var bottom = UiKit.HBox(10);
        bottom.AddChild(Cell($"{tier.BaseChips} × {tier.BaseMult:0.##}", 80, 16, UiKit.Text));
        var next = UiKit.MakeLabel($"next: +{tier.LevelChips} chips, +{tier.LevelMult:0.##} mult", 12, UiKit.TextMuted);
        next.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottom.AddChild(next);
        bottom.AddChild(thisPlay
            ? Cell("this play", 80, 12, UiKit.Selected, HorizontalAlignment.Right)
            : Cell(bought > 0 ? $"bought ×{bought}" : "not owned", 80, 12, bought > 0 ? UiKit.Text : UiKit.TextMuted, HorizontalAlignment.Right));
        lines.AddChild(bottom);
        return row;
    }

    private static Label Cell(string text, float width, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = UiKit.MakeLabel(text, size, color, align);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return label;
    }
}
