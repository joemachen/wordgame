using Godot;

namespace Wordgame.Godot;

/// <summary>
/// A tile button that can be dragged (carrying its tile id) and can accept a tile dropped onto it, forwarding the
/// drop to its owner. Clicks still work: Godot cancels the button press once a drag begins.
/// </summary>
public partial class TileButton : Button
{
    /// <summary>Set when any drag ends, so a click event fired at drag release can be ignored.</summary>
    public static ulong LastDragEndMs { get; private set; }

    public int TileId { get; set; } = -1;

    /// <summary>Builds the semi-transparent preview shown under the cursor; null disables dragging.</summary>
    public Func<Control>? DragPreviewFactory { get; set; }

    /// <summary>Invoked with this tile's id when a drag of it begins.</summary>
    public Action<int>? DragStarted { get; set; }

    /// <summary>Whether a dragged tile may currently be dropped onto this one; null rejects all drops.</summary>
    public Func<bool>? CanAcceptDrop { get; set; }

    /// <summary>(draggedTileId) — invoked when a tile is dropped onto this one.</summary>
    public Action<int>? Dropped { get; set; }

    /// <summary>True while this tile marks the slot a dragged tile will land in.</summary>
    public bool IsGhost { get; private set; }

    private Panel? _ghostOutline;

    public static bool RecentlyDragged => Time.GetTicksMsec() - LastDragEndMs < 150;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (DragPreviewFactory is null || TileId < 0)
            return default;

        var preview = DragPreviewFactory();
        preview.Modulate = new Color(1, 1, 1, 0.85f);
        // Centre the preview on the cursor, lifted a little so the ghost slot under it stays visible.
        var holder = new Control();
        holder.AddChild(preview);
        preview.Position = -preview.CustomMinimumSize / 2 - new Vector2(0, preview.CustomMinimumSize.Y * 0.3f);
        SetDragPreview(holder);
        DragStarted?.Invoke(TileId);
        return TileId;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.Int && CanAcceptDrop?.Invoke() == true;

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(data.AsInt32());

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
            LastDragEndMs = Time.GetTicksMsec();
    }

    /// <summary>Fades the tile to a see-through placeholder with an accent outline (or restores it).</summary>
    public void SetGhost(bool ghost)
    {
        IsGhost = ghost;
        SelfModulate = new Color(1, 1, 1, ghost ? 0.3f : 1f);
        foreach (var label in GetChildren().OfType<Label>())
            label.Modulate = new Color(1, 1, 1, ghost ? 0.45f : 1f);

        if (_ghostOutline is null && ghost)
        {
            _ghostOutline = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            _ghostOutline.AddThemeStyleboxOverride("panel", UiKit.Box(Colors.Transparent, 6, UiKit.Selected, 3, 0));
            _ghostOutline.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_ghostOutline);
        }
        if (_ghostOutline is not null)
            _ghostOutline.Visible = ghost;
    }
}

/// <summary>
/// The hand: accepts tiles dropped into the gaps between tiles, and slides tiles to their new places when one is
/// moved (record old positions, let the container lay out, then tween each tile from old to new).
/// </summary>
public partial class HandRow : HBoxContainer
{
    private const double SlideSeconds = 0.12;

    private readonly Dictionary<Control, Tween> _slides = new();
    private Dictionary<Control, Vector2>? _slideFrom;

    public HandRow()
    {
        MouseFilter = MouseFilterEnum.Pass;
        SortChildren += SlideToLayout;
    }

    public Func<bool>? CanAcceptDrop { get; set; }

    public Action<int>? Dropped { get; set; }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.Int && CanAcceptDrop?.Invoke() == true;

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(data.AsInt32());

    /// <summary>Moves a child to <paramref name="index"/>; the tiles it passes slide aside instead of jumping.</summary>
    public void MoveChildAnimated(Control child, int index)
    {
        if (child.GetIndex() == index)
            return;
        _slideFrom ??= GetChildren().OfType<Control>().ToDictionary(c => c, c => c.Position);
        MoveChild(child, index);
    }

    private void SlideToLayout()
    {
        if (_slideFrom is null)
            return;
        foreach (var stale in _slides.Keys.Where(c => !IsInstanceValid(c)).ToList())
            _slides.Remove(stale);
        foreach (var (child, from) in _slideFrom)
        {
            if (!IsInstanceValid(child) || child.GetParent() != this)
                continue;
            if (_slides.Remove(child, out var running))
                running.Kill();
            var to = child.Position;
            if (from == to)
                continue;
            child.Position = from;
            var tween = child.CreateTween();
            tween.TweenProperty(child, "position", to, SlideSeconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            _slides[child] = tween;
        }
        _slideFrom = null;
    }
}

/// <summary>An empty board square that accepts a dragged hand tile.</summary>
public partial class DropCell : Button
{
    public Action<int>? TileDropped { get; set; }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        TileDropped is not null && data.VariantType == Variant.Type.Int;

    public override void _DropData(Vector2 atPosition, Variant data) => TileDropped?.Invoke(data.AsInt32());
}
