using Crossword.Core.Effects;
using Crossword.Core.Scoring;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// The scoring receipt: a register tape beside the board that prints one line per scoring step while the ring-up
/// plays (<see cref="Main.AnimateScore"/>), with the running chips × mult beside each line, then a TOTAL that counts up
/// and punches. It lingers until the player touches a tile (<see cref="Dismiss"/>). Always in the layout, transparent
/// when idle, so the board never shifts. Numbers that tune it live in <see cref="Juice"/>; colours in <see cref="UiKit"/>.
/// </summary>
public partial class Receipt : PanelContainer
{
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _lines;
    private Label? _total;
    private Tween? _tween;

    /// <summary>Scoring lines printed so far (header and total excluded).</summary>
    public int LineCount { get; private set; }

    public bool HasTotal => _total is not null;

    public bool IsShowing { get; private set; }

    public string TotalText => _total?.Text ?? "";

    public Receipt()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Paper, radius: 3, border: UiKit.PaperEdge, borderWidth: 1, padding: 12));
        MouseFilter = MouseFilterEnum.Ignore;
        Modulate = new Color(1, 1, 1, 0);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Ignore };
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _lines = UiKit.VBox(4);
        _lines.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_lines);
        AddChild(_scroll);
    }

    /// <summary>Ink for a scoring source on paper: the sidebar's colour, darkened to read on newsprint.</summary>
    public static Color InkFor(string sourceId, Color uiColor) => sourceId switch
    {
        ScoringEngine.Sources.Tier => UiKit.PaperInk,
        ScoringEngine.Sources.Enhancement => uiColor.Darkened(0.45f),
        _ => uiColor.Darkened(0.35f),
    };

    /// <summary>Starts a fresh tape: fades the paper in and prints the header and the words played.</summary>
    public void Begin(string header, IEnumerable<string> words)
    {
        Clear();
        IsShowing = true;
        if (Juice.ReducedMotion)
            Modulate = Colors.White;
        else
            CreateTween().TweenProperty(this, "modulate:a", 1f, Juice.ReceiptPaperSeconds);
        _lines.AddChild(UiKit.MakeMonoLabel(header, 11, UiKit.PaperMuted));
        string played = string.Join(" + ", words);
        if (played.Length > 0)
        {
            var title = UiKit.MakeMonoLabel(played, 15, UiKit.PaperInk);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _lines.AddChild(title);
        }
        _lines.AddChild(Rule());
    }

    /// <summary>Prints one scoring step: its description on the left, the running chips × mult and product on the right.</summary>
    /// <returns>The printed row (fades and unrolls in unless reduced motion is on).</returns>
    public Control AddLine(EffectEvent e, Color ink)
    {
        var row = UiKit.HBox(8);
        var description = UiKit.MakeMonoLabel(e.Description, 12, ink);
        description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(description);
        var tally = UiKit.VBox(0);
        tally.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        tally.AddChild(UiKit.MakeMonoLabel($"{e.ChipsAfter:N0} × {e.MultAfter:0.##}", 12, ink, HorizontalAlignment.Right));
        long product = Math.Max(0, (long)decimal.Floor(e.ChipsAfter * e.MultAfter));
        tally.AddChild(UiKit.MakeMonoLabel($"= {product:N0}", 10, UiKit.PaperMuted, HorizontalAlignment.Right));
        row.AddChild(tally);
        Print(row);
        LineCount++;
        return row;
    }

    /// <summary>The perforated TOTAL line: the number counts up and punches; money earned prints under it.</summary>
    public void AddTotal(long total, int money, double seconds)
    {
        _lines.AddChild(Rule(perforated: true));
        var row = UiKit.HBox(8);
        var caption = UiKit.MakeMonoLabel("TOTAL", 15, UiKit.PaperInk);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        caption.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(caption);
        _total = UiKit.MakeMonoLabel(total.ToString("N0"), 20, UiKit.PaperInk, HorizontalAlignment.Right);
        _total.CustomMinimumSize = new Vector2(_total.GetMinimumSize().X, 0); // room for the final number while it counts up
        row.AddChild(_total);
        Print(row);
        Juice.CountUp(_total, 0, total, seconds, "N0");
        Juice.Pop(_total, Juice.ReceiptTotalPunch, seconds);
        if (money > 0)
            Print(UiKit.MakeMonoLabel($"+${money} earned", 12, UiKit.Money.Darkened(0.45f), HorizontalAlignment.Right));
    }

    /// <summary>Rolls the tape away (fades with reduced motion), then clears it. A no-op when nothing is printed.</summary>
    public void Dismiss()
    {
        if (!IsShowing)
            return;
        IsShowing = false;
        if (Juice.ReducedMotion)
        {
            Clear();
            return;
        }
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "modulate:a", 0f, Juice.ReceiptScrollOffSeconds);
        _tween.TweenProperty(_scroll, "position:x", _scroll.Position.X + Juice.ReceiptSlidePixels, Juice.ReceiptScrollOffSeconds)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _tween.Chain().TweenCallback(Callable.From(Clear));
    }

    /// <summary>Empties the tape at once.</summary>
    public void Clear()
    {
        _tween?.Kill();
        _tween = null;
        UiKit.ClearChildren(_lines);
        _scroll.Position = new Vector2(0, _scroll.Position.Y);
        LineCount = 0;
        _total = null;
        IsShowing = false;
        Modulate = new Color(1, 1, 1, 0);
    }

    /// <summary>Adds a row to the tape: it fades in and unrolls from its top edge (instant with reduced motion), and the end stays in view.</summary>
    private void Print(Control row)
    {
        _lines.AddChild(row);
        if (!Juice.ReducedMotion)
        {
            row.Modulate = new Color(1, 1, 1, 0);
            row.Scale = new Vector2(1, Juice.ReceiptUnrollStart);
            var tween = row.CreateTween().SetParallel();
            tween.TweenProperty(row, "scale", Vector2.One, Juice.ReceiptLineSeconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(row, "modulate:a", 1f, Juice.ReceiptLineSeconds);
        }
        Callable.From(() =>
        {
            if (IsInstanceValid(row) && row.IsInsideTree())
                _scroll.EnsureControlVisible(row);
        }).CallDeferred();
    }

    private static Control Rule(bool perforated = false)
    {
        var rule = UiKit.MakeMonoLabel(perforated ? "- - - - - - - - - - - - - - - - - - - -" : "····································", 10, UiKit.PaperMuted);
        rule.TextOverrunBehavior = TextServer.OverrunBehavior.TrimChar;
        return rule;
    }
}
