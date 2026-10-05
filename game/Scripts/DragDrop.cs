using Godot;

namespace Wordgame.Godot;

/// <summary>
/// A tile button that can be dragged (carrying its tile id) and can accept another tile dropped onto it,
/// which reorders the hand. Clicks still work: Godot cancels the button press once a drag begins.
/// </summary>
public partial class TileButton : Button
{
    /// <summary>Set when any drag ends, so a click event fired at drag release can be ignored.</summary>
    public static ulong LastDragEndMs { get; private set; }

    public int TileId { get; set; } = -1;

    /// <summary>Builds the semi-transparent preview shown under the cursor; null disables dragging.</summary>
    public Func<Control>? DragPreviewFactory { get; set; }

    /// <summary>(draggedTileId, thisTileId) — invoked when a tile is dropped onto this one.</summary>
    public Action<int, int>? TileDropped { get; set; }

    public static bool RecentlyDragged => Time.GetTicksMsec() - LastDragEndMs < 150;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (DragPreviewFactory is null || TileId < 0)
            return default;

        var preview = DragPreviewFactory();
        preview.Modulate = new Color(1, 1, 1, 0.85f);
        // Centre the preview on the cursor.
        var holder = new Control();
        holder.AddChild(preview);
        preview.Position = -preview.CustomMinimumSize / 2;
        SetDragPreview(holder);
        return TileId;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        TileDropped is not null && data.VariantType == Variant.Type.Int && data.AsInt32() != TileId;

    public override void _DropData(Vector2 atPosition, Variant data) => TileDropped?.Invoke(data.AsInt32(), TileId);

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
            LastDragEndMs = Time.GetTicksMsec();
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
