using Godot;

namespace Wordgame.Godot;

/// <summary>Game-wide settings (not per profile). Nothing here affects play.</summary>
public sealed record GameSettings
{
    /// <summary>The profile played last; launch picks it unless <c>--profile</c> says otherwise.</summary>
    public string? LastProfile { get; init; }
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
        return new SettingsStore(new GameSettings { LastProfile = last.Length > 0 ? last : null }, path);
    }

    /// <summary>Applies a change and saves it.</summary>
    public void Update(Func<GameSettings, GameSettings> change)
    {
        Settings = change(Settings);
        if (_path is null)
            return;
        var file = new ConfigFile();
        file.SetValue("profile", "last", Settings.LastProfile ?? "");
        if (file.Save(_path) is var error and not Error.Ok)
            GD.PushWarning($"Couldn't save the settings: {error}");
    }
}
