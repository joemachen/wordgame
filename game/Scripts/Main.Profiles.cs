using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Profile picker (title menu → Profile): every profile on disk, plus a box to create one. Each profile keeps its own
/// stats, unlocks and saved run; switching saves the current run first, then offers the new profile's run on the title.
/// </summary>
public partial class Main
{
    private const int MaxProfileName = 24;
    private Control _profilesOverlay = null!;
    private VBoxContainer _profilesBox = null!;
    private Label? _profilesError;

    // Where profiles and their saves live (the self-test points these at scratch folders).
    private string _profileRoot = ProfileStore.DefaultRoot;
    private string _saveRoot = RunSaveStore.DefaultRoot;

    private Control BuildProfilesOverlay()
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
        _profilesBox = UiKit.VBox(8);
        _profilesBox.CustomMinimumSize = new Vector2(460, 0);
        panel.AddChild(_profilesBox);
        centre.AddChild(panel);
        return overlay;
    }

    private void ShowProfiles()
    {
        RefreshProfiles();
        _profilesOverlay.Visible = true;
    }

    private void RefreshProfiles()
    {
        UiKit.ClearChildren(_profilesBox);
        _profilesBox.AddChild(UiKit.MakeLabel("Profiles", 26, UiKit.Text));
        _profilesBox.AddChild(UiKit.MakeLabel("Each profile keeps its own stats, unlocks and saved run.", 14, UiKit.TextMuted, wrap: true));

        string current = ProfileStore.SafeName(_profile.Profile.Name);
        var names = ProfileStore.List(_profileRoot);
        if (!names.Any(n => ProfileStore.SafeName(n) == current))
            names = names.Append(_profile.Profile.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string name in names)
        {
            bool isCurrent = ProfileStore.SafeName(name) == current;
            var button = UiKit.MakeButton(isCurrent ? $"{name}  (playing)" : name, isCurrent ? UiKit.Background : UiKit.PanelRaised, 18);
            button.Name = $"Profile_{ProfileStore.SafeName(name)}";
            button.Disabled = isCurrent;
            button.CustomMinimumSize = new Vector2(460, 44);
            button.Pressed += () => SwitchProfile(name);
            _profilesBox.AddChild(button);
        }

        _profilesBox.AddChild(new HSeparator());
        _profilesBox.AddChild(UiKit.MakeLabel("New profile", 16, UiKit.TextMuted));
        var row = UiKit.HBox(8);
        var field = UiKit.MakeLineEdit("Name", 340);
        field.Name = "ProfileNameField";
        field.MaxLength = MaxProfileName;
        field.TextSubmitted += CreateProfile;
        row.AddChild(field);
        var create = UiKit.MakeButton("Create", UiKit.Good.Darkened(0.25f), 16);
        create.Name = "CreateProfile";
        create.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        create.Pressed += () => CreateProfile(field.Text);
        row.AddChild(create);
        _profilesBox.AddChild(row);
        _profilesError = UiKit.MakeLabel("", 13, UiKit.Bad);
        _profilesBox.AddChild(_profilesError);
        _profilesBox.AddChild(UiKit.MakeLabel("Esc to close", 13, UiKit.TextMuted, HorizontalAlignment.Right));
    }

    /// <summary>Creates a profile and switches to it; a name that's already taken just switches to that profile.</summary>
    private void CreateProfile(string text)
    {
        string name = text.Trim();
        if (name.Length == 0 || name.All(c => !char.IsLetterOrDigit(c)))
        {
            if (_profilesError is not null)
                _profilesError.Text = "A name needs at least one letter or digit.";
            return;
        }
        string safe = ProfileStore.SafeName(name);
        SwitchProfile(ProfileStore.List(_profileRoot).FirstOrDefault(n => ProfileStore.SafeName(n) == safe) ?? name);
    }

    /// <summary>Saves the current run, then loads <paramref name="name"/>'s profile and saved run (if any).</summary>
    private void SwitchProfile(string name)
    {
        if (_session is not null)
            PersistRun();
        _profile = ProfileStore.Load(name, _profileRoot);
        _runSave = RunSaveStore.ForProfile(name, _saveRoot);
        _session = null!;
        _lastSavedSession = null;
        if (_runSave.Load(_baseConfig) is { } saved)
            Resume(saved);
        _settings.Update(s => s with { LastProfile = _profile.Profile.Name });
        _profilesOverlay.Visible = false;
        ShowTitle(_profile.Notice ?? _runSave.Notice);
    }
}
