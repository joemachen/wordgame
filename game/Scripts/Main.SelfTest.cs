using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// --selftest: drives the real UI through Godot's input pipeline (simulated mouse/keyboard events) to check
/// click-select, drag-to-reorder, drag-onto-board and shuffle. Prints PASS/FAIL lines, exits with code 0/1.
/// </summary>
public partial class Main
{
    private int _selfTestFailures;

    private async Task RunSelfTest()
    {
        await Frames(3);

        // 1. Click selects a hand tile.
        var first = HandButton(0);
        int firstId = first.TileId;
        await Click(Centre(first));
        Check("click selects tile", _selected.Any(t => t.Id == firstId));
        await Click(Centre(HandButton(0)));
        Check("second click deselects tile", _selected.Count == 0);

        // 2. Drag tile 0 onto tile 3 → it takes tile 3's slot (after it), without toggling selection.
        int targetId = HandButton(3).TileId;
        await Drag(Centre(HandButton(0)), Centre(HandButton(3)));
        var order = _handOrder.ToList();
        Check("drag right reorders hand", order.IndexOf(firstId) == 3 && order.IndexOf(targetId) == 2);
        Check("drag does not select", _selected.Count == 0);

        // 2b. Drag the last tile onto the first → it becomes first.
        int lastId = HandButton(_handRow.GetChildCount() - 1).TileId;
        await Drag(Centre(HandButton(_handRow.GetChildCount() - 1)), Centre(HandButton(0)));
        Check("drag left reorders hand", _handOrder[0] == lastId);

        // 3. Drag a hand tile onto an empty board square → it becomes pending there.
        int boardTileId = HandButton(0).TileId;
        var cellPos = new GridPos(3, 3);
        await Drag(Centre(HandButton(0)), Centre(BoardCell(cellPos)));
        Check("drag onto board places tile", _pending.TryGetValue(cellPos, out var placed) && placed.Id == boardTileId);
        Check("placed tile leaves the hand row", _handRow.GetChildCount() == Round.Hand.Count - 1);

        // 4. Space shuffles the remaining arrangement.
        var before = _handOrder.ToList();
        await PressKey(global::Godot.Key.Space);
        Check("space shuffles hand", !before.SequenceEqual(_handOrder) && before.Order().SequenceEqual(_handOrder.Order()));

        // 5. Esc recalls the pending tile.
        await PressKey(global::Godot.Key.Escape);
        Check("escape recalls pending tiles", _pending.Count == 0 && _handRow.GetChildCount() == Round.Hand.Count);

        GD.Print(_selfTestFailures == 0 ? "SELFTEST: ALL PASSED" : $"SELFTEST: {_selfTestFailures} FAILED");
        GetTree().Quit(_selfTestFailures == 0 ? 0 : 1);
    }

    private void Check(string name, bool ok)
    {
        if (!ok)
            _selfTestFailures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {name}");
    }

    private TileButton HandButton(int index) => _handRow.GetChild<TileButton>(index);

    /// <summary>The board grid is rebuilt on refresh; find the control at a grid position.</summary>
    private Control BoardCell(GridPos pos)
    {
        var grid = _boardHolder.GetChild(0).GetChild<GridContainer>(0);
        return grid.GetChild<Control>(pos.Row * Round.Board.Size + pos.Col);
    }

    private static Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e, inLocalCoords: true);

    private InputEventMouseButton Button(Vector2 at, bool pressed) => new()
    {
        Position = at,
        GlobalPosition = at,
        ButtonIndex = MouseButton.Left,
        Pressed = pressed,
        ButtonMask = pressed ? MouseButtonMask.Left : 0,
    };

    private async Task Click(Vector2 at)
    {
        Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Push(Button(at, pressed: true));
        await Frames(1);
        Push(Button(at, pressed: false));
        await Frames(3);
    }

    private async Task Drag(Vector2 from, Vector2 to)
    {
        Push(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        Push(Button(from, pressed: true));
        await Frames(1);
        var last = from;
        for (int step = 1; step <= 12; step++)
        {
            var at = from.Lerp(to, step / 12f);
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - last, ButtonMask = MouseButtonMask.Left });
            last = at;
            await Frames(1);
        }
        Push(Button(to, pressed: false));
        await Frames(3);
    }

    private async Task PressKey(global::Godot.Key key)
    {
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Push(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }
}
