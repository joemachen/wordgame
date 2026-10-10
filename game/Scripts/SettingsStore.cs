using Godot;

namespace Wordgame.Godot;

/// <summary>Game-wide settings (not per profile). Nothing here affects play.</summary>
public sealed record GameSettings
{
    /// <summary>Text sizes offered on the Settings page (font size multipliers, <see cref="UiKit.TextScale"/>).</summary>
    public static readonly float[] TextScales = [0.9f, 1f, 1.1f, 1.2f]; // 130%+ overflows the 1440×900 layout until it can reflow

    /// <summary>The profile played last; launch picks it unless <c>--profile</c> says otherwise.</summary>
    public string? LastProfile { get; init; }

    /// <summary>No screen shake, confetti, stamp slam or bouncing numbers (<see cref="Juice.ReducedMotion"/>).</summary>
    public bool ReducedMotion { get; init; }

    public bool Fullscreen { get; init; }

    /// <summary>Scales every font; one of <see cref="TextScales"/>.</summary>
    public float TextScale { get; init; } = 1f;
}

/// <summary>
/// Loads and saves <see cref="GameSettings"/> as <c>user://settings.cfg</c> (Godot <see cref="ConfigFile"/>). An
/// in-memory store (QA flags) never touches disk.
/// </summary>
public sealed class SettingsStore
{
    private const string DefaultPath = "user://settings.cfg";
    private readonly string? _path;

    private SettingsStore(GameSettings settings, string? path)
    {
        Settings = settings;
        _path = path;
    }

    public GameSettings Settings { get; private set; }

    public static SettingsStore InMemory() => new(new GameSettings(), null);

    public static SettingsStore Load(string path = DefaultPath)
    {
        var file = new ConfigFile();
        if (file.Load(path) != Error.Ok)
            return new SettingsStore(new GameSettings(), path);
        string last = file.GetValue("profile", "last", "").AsString();
        float scale = (float)file.GetValue("display", "text_scale", 1.0).AsDouble();
        return new SettingsStore(new GameSettings
        {
            LastProfile = last.Length > 0 ? last : null,
            ReducedMotion = file.GetValue("accessibility", "reduced_motion", false).AsBool(),
            Fullscreen = file.GetValue("display", "fullscreen", false).AsBool(),
            TextScale = GameSettings.TextScales.MinBy(s => Math.Abs(s - scale)),
        }, path);
    }

    /// <summary>Applies a change and saves it.</summary>
    public void Update(Func<GameSettings, GameSettings> change)
    {
        Settings = change(Settings);
        if (_path is null)
            return;
        var file = new ConfigFile();
        file.SetValue("profile", "last", Settings.LastProfile ?? "");
        file.SetValue("accessibility", "reduced_motion", Settings.ReducedMotion);
        file.SetValue("display", "fullscreen", Settings.Fullscreen);
        file.SetValue("display", "text_scale", Settings.TextScale);
        if (file.Save(_path) is var error and not Error.Ok)
            GD.PushWarning($"Couldn't save the settings: {error}");
    }
}
