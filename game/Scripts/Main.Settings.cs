using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Settings page (title menu → Settings): reduced motion, fullscreen and text size. Changes apply and save at once
/// (<see cref="SettingsStore"/>); a new text size rebuilds the whole UI. Sound settings come with sound.
/// </summary>
public partial class Main
{
    private Control _settingsOverlay = null!;
    private VBoxContainer _settingsBox = null!;

    private Control BuildSettingsOverlay()
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
        _settingsBox = UiKit.VBox(12);
        _settingsBox.CustomMinimumSize = new Vector2(620, 0);
        panel.AddChild(_settingsBox);
        centre.AddChild(panel);
        return overlay;
    }

    private void ShowSettings()
    {
        RefreshSettings();
        _settingsOverlay.Visible = true;
    }

    private void RefreshSettings()
    {
        var settings = _settings.Settings;
        UiKit.ClearChildren(_settingsBox);
        _settingsBox.AddChild(UiKit.MakeLabel("Settings", 26, UiKit.Text));

        _settingsBox.AddChild(SettingRow("Reduced motion", "No screen shake, confetti, stamp slam or bouncing numbers.",
            Toggle("Setting_ReducedMotion", settings.ReducedMotion, on => ChangeSettings(s => s with { ReducedMotion = on }))));
        _settingsBox.AddChild(SettingRow("Fullscreen", "Fill the screen instead of a window.",
            Toggle("Setting_Fullscreen", settings.Fullscreen, on => ChangeSettings(s => s with { Fullscreen = on }))));

        var sizes = UiKit.HBox(6);
        foreach (float scale in GameSettings.TextScales)
        {
            bool selected = Math.Abs(scale - settings.TextScale) < 0.001f;
            var button = UiKit.MakeButton($"{scale * 100:0}%", selected ? UiKit.Selected.Darkened(0.45f) : UiKit.PanelRaised, 15);
            button.Name = $"TextScale_{scale * 100:0}";
            button.CustomMinimumSize = new Vector2(66, 36);
            button.Pressed += () => ChangeTextScale(scale);
            sizes.AddChild(button);
        }
        _settingsBox.AddChild(SettingRow("Text size", "Scales the text (tiles keep their size).", sizes));
        _settingsBox.AddChild(SettingRow("Sound", "Volume settings come with sound effects.", UiKit.MakeLabel("—", 15, UiKit.TextMuted)));
        _settingsBox.AddChild(UiKit.MakeLabel("Esc to close", 13, UiKit.TextMuted, HorizontalAlignment.Right));
    }

    private static Control SettingRow(string name, string description, Control control)
    {
        var row = UiKit.HBox(16);
        var text = UiKit.VBox(2);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(UiKit.MakeLabel(name, 18, UiKit.Text));
        text.AddChild(UiKit.MakeLabel(description, 13, UiKit.TextMuted));
        row.AddChild(text);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(control);
        return row;
    }

    private static Button Toggle(string name, bool on, Action<bool> changed)
    {
        var button = UiKit.MakeButton(on ? "On" : "Off", on ? UiKit.Good.Darkened(0.25f) : UiKit.PanelRaised, 15);
        button.Name = name;
        button.CustomMinimumSize = new Vector2(80, 36);
        button.Pressed += () => changed(!on);
        return button;
    }

    private void ChangeSettings(Func<GameSettings, GameSettings> change)
    {
        _settings.Update(change);
        ApplySettings();
        RefreshSettings();
    }

    /// <summary>Saves a new text size and rebuilds the UI with it (only reachable from the title, so no play is lost).</summary>
    private void ChangeTextScale(float scale)
    {
        _settings.Update(s => s with { TextScale = scale });
        ApplySettings();
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        BuildLayout();
        Refresh();
        ShowTitle();
        ShowSettings();
    }

    /// <summary>Puts the settings into effect (on launch, before the UI is built, and after every change).</summary>
    private void ApplySettings()
    {
        var settings = _settings.Settings;
        Juice.ReducedMotion = settings.ReducedMotion;
        UiKit.TextScale = settings.TextScale;
        var mode = settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() is var current && (current == DisplayServer.WindowMode.Fullscreen) != settings.Fullscreen)
            DisplayServer.WindowSetMode(mode);
    }
}
