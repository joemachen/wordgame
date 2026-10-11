using Crossword.Core.Domain;
using Godot;

namespace Wordgame.Godot;

/// <summary>Colours and widget factories. The whole UI is built in code so it stays diff-friendly.</summary>
public static class UiKit
{
    /// <summary>The player's Text size setting: multiplies every font size (tile faces stay sized to their tile).</summary>
    public static float TextScale { get; set; } = 1f;

    /// <summary>A font size with <see cref="TextScale"/> applied.</summary>
    public static int FontSize(int size) => Math.Max(1, (int)Math.Round(size * TextScale));

    // Newsroom palette: dark ink desk, newsprint tiles, red/blue editor's pencils for Mult/Chips.
    public static readonly Color Background = new("161a22");
    public static readonly Color Panel = new("222836");
    public static readonly Color PanelRaised = new("2d3446");
    public static readonly Color PanelBorder = new("3d465c");
    public static readonly Color Text = new("e8e4da");
    public static readonly Color TextMuted = new("9aa1b2");
    public static readonly Color Newsprint = new("f2ead3");
    public static readonly Color Ink = new("1c1c1c");
    public static readonly Color Pending = new("ffe08a");
    public static readonly Color Selected = new("ffd166");
    public static readonly Color Chips = new("3d8bfd");
    public static readonly Color Mult = new("e5484d");
    public static readonly Color Money = new("f5c542");
    public static readonly Color Good = new("4cc38a");
    public static readonly Color Bad = new("ff6b6b");
    public static readonly Color Cell = new("2a3142");
    public static readonly Color Blocked = new("07090d");

    // Paper: the scoring receipt and the (hidden) clue columns print on newsprint.
    public static readonly Color Paper = new("f6f1e4");
    public static readonly Color PaperInk = new("2b2620");
    public static readonly Color PaperMuted = new("6b6253");
    public static readonly Color PaperEdge = new("cfc4a8");

    /// <summary>Typewriter face for the receipt (a system font for now; the visual overhaul brings an open-license one).</summary>
    public static readonly Font Mono = new SystemFont { FontNames = ["Consolas", "Courier New", "monospace"] };

    public static Color PremiumColor(Premium premium) => premium switch
    {
        Premium.DoubleLetter => new Color("5aa9e6"),
        Premium.TripleLetter => new Color("2f6fb5"),
        Premium.DoubleWord => new Color("d9688f"),
        Premium.TripleWord => new Color("c0392b"),
        _ => Cell,
    };

    public static string PremiumText(Premium premium) => premium switch
    {
        Premium.DoubleLetter => "2L",
        Premium.TripleLetter => "3L",
        Premium.DoubleWord => "2W",
        Premium.TripleWord => "3W",
        _ => "",
    };

    public static Color EnhancementColor(TileEnhancement enhancement) => enhancement switch
    {
        TileEnhancement.Bold => new Color("3d8bfd"),
        TileEnhancement.Italic => new Color("b45cff"),
        TileEnhancement.Gilded => new Color("f5c542"),
        _ => new Color("c9bfa5"),
    };

    public static StyleBoxFlat Box(Color bg, int radius = 8, Color? border = null, int borderWidth = 0, int padding = 8, int shadow = 0)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = border ?? bg };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(borderWidth);
        box.SetContentMarginAll(padding);
        if (shadow > 0)
        {
            box.ShadowColor = new Color(0, 0, 0, 0.45f);
            box.ShadowSize = shadow;
            box.ShadowOffset = new Vector2(0, shadow / 2f);
        }
        return box;
    }

    public static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false,
        bool scaled = true)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", scaled ? FontSize(size) : size);
        label.AddThemeColorOverride("font_color", color);
        if (wrap)
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    /// <summary>A label in the typewriter face (<see cref="Mono"/>).</summary>
    public static Label MakeMonoLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = MakeLabel(text, size, color, align);
        label.AddThemeFontOverride("font", Mono);
        return label;
    }

    public static Button MakeButton(string text, Color bg, int fontSize = 18, Color? textColor = null)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", FontSize(fontSize));
        var fg = textColor ?? Text;
        button.AddThemeColorOverride("font_color", fg);
        button.AddThemeColorOverride("font_hover_color", fg);
        button.AddThemeColorOverride("font_pressed_color", fg);
        button.AddThemeColorOverride("font_disabled_color", TextMuted);
        button.AddThemeStyleboxOverride("normal", Box(bg, padding: 10));
        button.AddThemeStyleboxOverride("hover", Box(bg.Lightened(0.15f), padding: 10));
        button.AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.2f), padding: 10));
        button.AddThemeStyleboxOverride("disabled", Box(bg.Darkened(0.5f), padding: 10));
        return button;
    }

    /// <summary>A one-line text box in the panel style; the border lights up while it has focus.</summary>
    public static LineEdit MakeLineEdit(string placeholder, float width, int fontSize = 16)
    {
        var edit = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(width, 0) };
        edit.AddThemeFontSizeOverride("font_size", FontSize(fontSize));
        edit.AddThemeColorOverride("font_color", Text);
        edit.AddThemeColorOverride("font_placeholder_color", TextMuted);
        edit.AddThemeColorOverride("caret_color", Selected);
        edit.AddThemeStyleboxOverride("normal", Box(Background, 6, PanelBorder, 1, 8));
        edit.AddThemeStyleboxOverride("focus", Box(Background, 6, Selected, 2, 8));
        return edit;
    }

    public static PanelContainer MakePanel(Color bg, int padding = 12, int radius = 10, Color? border = null, int borderWidth = 0)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(bg, radius, border, borderWidth, padding));
        return panel;
    }

    public static VBoxContainer VBox(int separation = 8) => WithSeparation(new VBoxContainer(), separation);

    public static HBoxContainer HBox(int separation = 8) => WithSeparation(new HBoxContainer(), separation);

    private static T WithSeparation<T>(T box, int separation) where T : BoxContainer
    {
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>A clickable tile: big letter, small value in the corner, coloured border for enhancements.</summary>
    /// <param name="blankWild">Show a wild tile as "?" (in the hand or deck); otherwise it shows the letter it plays as.</param>
    public static TileButton MakeTile(Tile tile, int value, float size, Color background, bool raised = false, bool blankWild = false)
    {
        var button = new TileButton { TileId = tile.Id, CustomMinimumSize = new Vector2(size, size), FocusMode = Control.FocusModeEnum.None };
        int borderWidth = tile.Enhancement == TileEnhancement.None ? 2 : 4;
        var border = EnhancementColor(tile.Enhancement);
        button.AddThemeStyleboxOverride("normal", Box(background, 6, border, borderWidth, 0, raised ? 8 : 3));
        button.AddThemeStyleboxOverride("hover", Box(background.Lightened(0.1f), 6, border, borderWidth, 0, 6));
        button.AddThemeStyleboxOverride("pressed", Box(background.Darkened(0.1f), 6, border, borderWidth, 0, 2));
        button.AddThemeStyleboxOverride("disabled", Box(background, 6, border, borderWidth, 0, 0));

        string face = tile.IsWild && blankWild ? "?" : tile.Letter.ToString();
        var letter = MakeLabel(face, (int)(size * 0.5f), tile.IsWild ? Chips.Darkened(0.25f) : Ink, HorizontalAlignment.Center, scaled: false);
        letter.VerticalAlignment = VerticalAlignment.Center;
        letter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(letter);

        var points = MakeLabel(value.ToString(), (int)(size * 0.2f), new Color("5b5545"), HorizontalAlignment.Right, scaled: false);
        points.VerticalAlignment = VerticalAlignment.Bottom;
        points.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        points.OffsetRight = -5;
        points.OffsetBottom = -2;
        button.AddChild(points);

        if (tile.IsWild)
        {
            var wild = MakeLabel("wild", (int)(size * 0.17f), Chips.Darkened(0.25f), HorizontalAlignment.Left, scaled: false);
            wild.VerticalAlignment = VerticalAlignment.Bottom;
            wild.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            wild.OffsetLeft = 5;
            wild.OffsetBottom = -2;
            wild.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.AddChild(wild);
        }

        if (tile.Enhancement != TileEnhancement.None)
        {
            var tag = MakeLabel(tile.Enhancement.ToString()[..1], (int)(size * 0.2f), border, HorizontalAlignment.Left);
            tag.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            tag.OffsetLeft = 5;
            tag.OffsetTop = 1;
            button.AddChild(tag);
        }
        return button;
    }

    /// <summary>Strikes through a hand tile whose letter is censored this round (Censored Press).</summary>
    public static void MarkCensored(TileButton button)
    {
        var strike = new ColorRect { Name = "CensoredStrike", Color = Bad, MouseFilter = Control.MouseFilterEnum.Ignore };
        strike.SetAnchorsPreset(Control.LayoutPreset.HcenterWide);
        strike.OffsetTop = -2;
        strike.OffsetBottom = 2;
        strike.OffsetLeft = 6;
        strike.OffsetRight = -6;
        button.AddChild(strike);
        button.Modulate = new Color(1, 1, 1, 0.75f);
    }

    public static readonly Color Fresh = new("7ee0b5");

    /// <summary>Marks a freshly drawn hand tile: an accent outline and a small NEW tag.</summary>
    public static void MarkNew(TileButton button)
    {
        var outline = new Panel { Name = "NewOutline", MouseFilter = Control.MouseFilterEnum.Ignore };
        outline.AddThemeStyleboxOverride("panel", Box(Colors.Transparent, 6, Fresh, 3, 0));
        outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(outline);
        var tag = MakeLabel("NEW", 11, Fresh.Darkened(0.45f), HorizontalAlignment.Right);
        tag.Name = "NewTag";
        tag.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        tag.OffsetRight = -5;
        tag.OffsetTop = 2;
        tag.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.AddChild(tag);
    }

    /// <summary>Fades and pops a control in (scale only — containers own its position; reduced motion: fade only).</summary>
    public static void DropIn(Control control, float size)
    {
        control.PivotOffset = new Vector2(size / 2, size / 2);
        control.Scale = Juice.ReducedMotion ? Vector2.One : new Vector2(0.6f, 0.6f);
        control.Modulate = new Color(1, 1, 1, 0);
        var tween = control.CreateTween().SetParallel();
        tween.TweenProperty(control, "scale", Vector2.One, 0.28).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "modulate:a", 1f, 0.2);
    }

    public static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
