using Crossword.Core.Domain;
using Godot;
using GridPos = Crossword.Core.Domain.Position;

namespace Wordgame.Godot;

/// <summary>
/// Letter picker for wild tiles: placing a wild from the hand asks which letter it plays as (click a letter or type
/// it; Esc or Cancel puts it back). The chosen letter only lives on the pending tile until the play is submitted.
/// </summary>
public partial class Main
{
    private Control _wildOverlay = null!;
    private (GridPos Position, Tile Tile)? _wildTarget;

    private Control BuildWildOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);
        var panel = UiKit.MakePanel(UiKit.Panel, padding: 20, radius: 12, border: UiKit.Chips, borderWidth: 2);
        centre.AddChild(panel);
        var box = UiKit.VBox(12);
        panel.AddChild(box);
        box.AddChild(UiKit.MakeLabel("Play the wild tile as…", 24, UiKit.Text));
        box.AddChild(UiKit.MakeLabel("Click a letter or type it. A wild tile scores 0 chips whatever it plays as.", 14, UiKit.TextMuted));

        var grid = new GridContainer { Columns = 9 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            char letter = c;
            var button = UiKit.MakeButton(letter.ToString(), UiKit.Newsprint, 22, UiKit.Ink);
            button.Name = $"Wild{letter}";
            button.CustomMinimumSize = new Vector2(48, 48);
            button.Pressed += () => ChooseWildLetter(letter);
            grid.AddChild(button);
        }
        box.AddChild(grid);

        var cancel = UiKit.MakeButton("Cancel  Esc", UiKit.PanelRaised, 16);
        cancel.Pressed += CancelWildLetter;
        box.AddChild(cancel);
        return overlay;
    }

    /// <summary>Asks which letter the wild <paramref name="tile"/> plays as on <paramref name="pos"/>.</summary>
    private void AskWildLetter(GridPos pos, Tile tile)
    {
        _wildTarget = (pos, tile);
        foreach (var button in _wildOverlay.FindChildren("Wild?", nameof(Button), owned: false).OfType<Button>())
            button.Disabled = button.Name == $"Wild{Round.Config.CensoredLetter}";
        _wildOverlay.Visible = true;
    }

    private void ChooseWildLetter(char letter)
    {
        if (_wildTarget is not { } target)
            return;
        if (letter == Round.Config.CensoredLetter)
        {
            SetMessage($"{letter} is censored this round.", UiKit.Bad);
            return;
        }
        _wildTarget = null;
        _wildOverlay.Visible = false;
        _selected.RemoveAll(t => t.Id == target.Tile.Id);
        foreach (var moved in _pending.Where(kv => kv.Value.Id == target.Tile.Id).Select(kv => kv.Key).ToList())
            _pending.Remove(moved);
        _pending[target.Position] = target.Tile.As(Letter.From(letter));
        Refresh();
    }

    private void CancelWildLetter()
    {
        _wildTarget = null;
        _wildOverlay.Visible = false;
        Refresh();
    }

    /// <summary>Keyboard while the picker is open: a letter picks it, Esc cancels. Returns true if handled.</summary>
    private bool HandleWildKey(InputEventKey key)
    {
        if (!_wildOverlay.Visible)
            return false;
        char typed = key.Unicode != 0 ? char.ToUpperInvariant((char)key.Unicode)
            : key.Keycode is >= Key.A and <= Key.Z ? (char)('A' + (key.Keycode - Key.A)) : '_';
        if (typed is >= 'A' and <= 'Z')
            ChooseWildLetter(typed);
        else if (key.Keycode == Key.Escape)
            CancelWildLetter();
        return true;
    }
}
