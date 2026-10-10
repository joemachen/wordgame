using System.Text;
using Crossword.Core.Run;
using Crossword.Core.Save;
using Godot;

namespace Wordgame.Godot;

/// <summary>
/// Keeps the run in progress as <c>user://saves/&lt;profile&gt;.json</c> so closing the game never loses it. An
/// in-memory store (QA and dev-setup flags) never touches disk, so those runs can't replace the player's run. An
/// unreadable save is moved aside to <c>.bak</c> rather than overwritten.
/// </summary>
public sealed class RunSaveStore
{
    private readonly string? _path;
    private string? _memory;

    private RunSaveStore(string? path) => _path = path;

    /// <summary>A message about the last load (e.g. a corrupt save was set aside), or null.</summary>
    public string? Notice { get; private set; }

    public static RunSaveStore InMemory() => new(null);

    public const string DefaultRoot = "user://saves";

    public static RunSaveStore ForProfile(string profileName, string root = DefaultRoot) =>
        AtPath(Path.Combine(ProjectSettings.GlobalizePath(root), ProfileStore.SafeName(profileName) + ".json"));

    public static RunSaveStore AtPath(string path) => new(path);

    /// <summary>The saved run, or null if there is none or it can't be read (then <see cref="Notice"/> says why).</summary>
    public SavedRun? Load(RunConfig config)
    {
        Notice = null;
        string? json;
        try
        {
            json = _path is null ? _memory : File.Exists(_path) ? File.ReadAllText(_path, Encoding.UTF8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Couldn't read the saved run: {e.Message}");
            Notice = "Your saved run couldn't be read, so a new run was started.";
            return null;
        }
        if (json is null)
            return null;

        var loaded = RunSaveJson.Deserialize(json, config);
        if (loaded.IsOk)
            return loaded.Value;

        _memory = null;
        string kept = "";
        if (_path is not null)
        {
            try
            {
                string backup = _path + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Move(_path, backup, overwrite: true);
                kept = $" (the old file is kept as {Path.GetFileName(backup)})";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Couldn't set the unreadable save aside: {e.Message}");
            }
        }
        GD.PushWarning($"Saved run couldn't be loaded: {loaded.Error}");
        Notice = $"Your saved run couldn't be loaded, so a new run was started{kept}.";
        return null;
    }

    /// <summary>Saves the run (write to a temp file, then replace).</summary>
    public void Save(GameSession session, IReadOnlyList<int> handOrder)
    {
        string json = RunSaveJson.Serialize(session, handOrder);
        if (_path is null)
        {
            _memory = json;
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Couldn't save the run: {e.Message}");
        }
    }

    /// <summary>Forgets the saved run (it was lost).</summary>
    public void Delete()
    {
        _memory = null;
        if (_path is null)
            return;
        try
        {
            File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Couldn't delete the saved run: {e.Message}");
        }
    }
}
