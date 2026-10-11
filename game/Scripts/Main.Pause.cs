using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Pause screen: Esc with no menu open (and, in a round, nothing placed or selected) brings it up over the game.
/// Resume, Settings (opens on top; closing it lands back here), Main menu, Quit. Esc or a click outside resumes.
/// </summary>
public partial class Main
{
    private Control _pauseOverlay = null!;
    private Button _pauseResume = null!;
    private Button _pauseSettings = null!;
    private Button _pauseHowToPlay = null!;
    private Button _pauseMainMenu = null!;

    private Control BuildPauseOverlay()
    {
        var overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                HidePause(); // click outside the panel resumes
        };

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(centre);

        var panel = UiKit.MakePanel(UiKit.Panel, padding: 28, radius: 12, border: UiKit.Chips, borderWidth: 2);
        panel.MouseFilter = MouseFilterEnum.Stop;
        var box = UiKit.VBox(12);
        panel.AddChild(box);
        centre.AddChild(panel);

        box.AddChild(UiKit.MakeLabel("Paused", 30, UiKit.Text, HorizontalAlignment.Center));
        _pauseResume = PauseButton(box, "Resume", UiKit.Good.Darkened(0.25f), HidePause);
        _pauseHowToPlay = PauseButton(box, "How to play", UiKit.PanelRaised, ShowHowToPlay);
        _pauseSettings = PauseButton(box, "Settings", UiKit.PanelRaised, ShowSettings);
        _pauseMainMenu = PauseButton(box, "Main menu", UiKit.PanelRaised, () => ShowTitle());
        PauseButton(box, "Quit", UiKit.PanelRaised, QuitGame);
        box.AddChild(UiKit.MakeLabel("Esc to resume", 13, UiKit.TextMuted, HorizontalAlignment.Center));
        return overlay;
    }

    private static Button PauseButton(VBoxContainer box, string text, Color color, Action pressed)
    {
        var button = UiKit.MakeButton(text, color, 20);
        button.CustomMinimumSize = new Vector2(320, 50);
        button.Pressed += pressed;
        box.AddChild(button);
        return button;
    }

    private void ShowPause()
    {
        if (_session is null)
            return;
        CloseStyleGuides();
        _pauseOverlay.Visible = true;
    }

    private void HidePause() => _pauseOverlay.Visible = false;
}
