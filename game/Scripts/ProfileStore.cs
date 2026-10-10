using System.Text;
using Crossword.Core.Profile;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Loads and saves the player profile as <c>user://profiles/&lt;name&gt;.json</c>. An in-memory store (dev flags,
/// self-test) never touches disk, so QA runs don't pollute real stats. An unreadable file is moved aside to
/// <c>.bak</c> rather than overwritten.
/// </summary>
public sealed class ProfileStore
{
    private readonly string? _path;

    private ProfileStore(PlayerProfile profile, string? path)
    {
        Profile = profile;
        _path = path;
    }

    public PlayerProfile Profile { get; private set; }

    /// <summary>A message about the load (e.g. a corrupt file was set aside), or null.</summary>
    public string? Notice { get; private init; }

    public const string DefaultRoot = "user://profiles";

    public static ProfileStore InMemory(string name) => new(PlayerProfile.New(name), null);

    public static ProfileStore Load(string name, string root = DefaultRoot)
    {
        string safe = SafeName(name);
        string dir = ProjectSettings.GlobalizePath(root);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, safe + ".json");
        if (!File.Exists(path))
            return new ProfileStore(PlayerProfile.New(name), path);

        var loaded = ProfileJson.Deserialize(File.ReadAllText(path, Encoding.UTF8));
        if (loaded.IsOk)
            return new ProfileStore(loaded.Value, path);

        string backup = path + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Move(path, backup);
        GD.PushWarning($"Profile '{safe}' couldn't be read ({loaded.Error}); moved it to {backup} and started fresh.");
        return new ProfileStore(PlayerProfile.New(name), path) { Notice = $"Your profile couldn't be read, so a fresh one was started (the old file is kept as {Path.GetFileName(backup)})." };
    }

    /// <summary>Applies a stats change and saves (write to a temp file, then replace).</summary>
    public void Update(Func<PlayerStats, PlayerStats> change)
    {
        Profile = Profile with { Stats = change(Profile.Stats) };
        if (_path is null)
            return;
        try
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, ProfileJson.Serialize(Profile), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException e)
        {
            GD.PushWarning($"Couldn't save the profile: {e.Message}");
        }
    }

    /// <summary>The names of the profiles saved under <paramref name="root"/> (not backups), sorted.</summary>
    public static List<string> List(string root = DefaultRoot)
    {
        string dir = ProjectSettings.GlobalizePath(root);
        if (!Directory.Exists(dir))
            return [];
        var names = new List<string>();
        foreach (string path in Directory.GetFiles(dir, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (ProfileJson.Deserialize(File.ReadAllText(path, Encoding.UTF8)) is { IsOk: true } loaded)
                    name = loaded.Value.Name;
            }
            catch (IOException)
            {
                // listed by its file name
            }
            names.Add(name);
        }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string SafeName(string name)
    {
        var chars = name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        return chars.Length == 0 ? "Player" : new string(chars);
    }
}
