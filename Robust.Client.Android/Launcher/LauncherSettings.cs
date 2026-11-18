using System.Text.Json;
using Robust.Launcher.Core;

namespace Robust.Client.Android.Launcher;

/// <summary>
/// Persistent launcher state, stored as JSON in the app-private files dir.
/// </summary>
public sealed class LauncherSettings
{
    public LoginInfo? Login { get; set; }
    public string GuestName { get; set; } = "";
    public List<FavoriteServer> Favorites { get; set; } = new();
    public string HubUrl { get; set; } = ServerApi.DefaultHubUrl;
    public string EngineReleaseBase { get; set; } = EngineManager.DefaultReleaseBase;
    /// <summary>0 = automatic (from screen density).</summary>
    public float UiScale { get; set; }
    public bool TouchControls { get; set; } = true;
    public int TouchOpacity { get; set; } = 60;
    public float TouchScale { get; set; } = 1f;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath(string filesDir) => Path.Combine(filesDir, "launcher-settings.json");

    public static LauncherSettings Load(string filesDir)
    {
        try
        {
            var path = FilePath(filesDir);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("Launcher", $"Failed to load settings: {e}");
        }

        return new LauncherSettings();
    }

    public void Save(string filesDir)
    {
        var path = FilePath(filesDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class FavoriteServer
{
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
}
