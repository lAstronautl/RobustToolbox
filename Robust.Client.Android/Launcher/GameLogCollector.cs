using System.Text;

namespace Robust.Client.Android.Launcher;

/// <summary>
/// Collects what happened in the game process without adb: our own log file plus logcat.
/// Apps may read logcat lines of their own UID, and the ":game" process shares the app's UID.
/// The engine's console log goes straight to logcat on Android (Console.OpenStandardOutput is a logcat stream),
/// and native crashes are reported there too ("F libc: Fatal signal ...").
/// </summary>
internal static class GameLogCollector
{
    public static string LogFile(string filesDir) => Path.Combine(filesDir, "last-game.log");
    public static string LogcatSnapshot(string filesDir) => Path.Combine(filesDir, "last-game-logcat.txt");

    private static readonly string[] Tags =
    {
        "DOTNET", "RobustGame", "RobustHost", "mono", "mono-rt", "SDL", "libc", "DEBUG", "AndroidRuntime",
        "lowmemorykiller", "lmkd", "ActivityManager", "Adreno", "libEGL", "OpenGLRenderer", "ALSOFT", "openal",
    };

    /// <summary>Reads this app's logcat and keeps the lines relevant to the game.</summary>
    public static string ReadLogcat()
    {
        try
        {
            var proc = Java.Lang.Runtime.GetRuntime()!.Exec(new[] { "logcat", "-d", "-v", "time", "-t", "4000" })!;
            using var reader = new StreamReader(proc.InputStream!);
            var all = reader.ReadToEnd();
            proc.WaitFor();

            var sb = new StringBuilder();
            foreach (var line in all.Split('\n'))
            {
                if (Tags.Any(t => line.Contains($"/{t}(") || line.Contains($"/{t} ")) || line.Contains("Fatal signal")
                    || line.Contains("[mem]") || line.Contains("Robust") || line.Contains(":game"))
                    sb.AppendLine(line.TrimEnd('\r'));
            }

            return sb.Length == 0 ? "(logcat пуст)" : sb.ToString();
        }
        catch (Exception e)
        {
            return $"(не удалось прочитать logcat: {e.Message})";
        }
    }

    public static void SnapshotLogcat(string filesDir)
    {
        try
        {
            File.WriteAllText(LogcatSnapshot(filesDir), ReadLogcat());
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>Host log file + logcat (saved snapshot if there is one, else read now), trimmed to the last lines.</summary>
    public static string Collect(string filesDir, int maxLines = 600)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== last-game.log ===");
        try
        {
            using var fs = new FileStream(LogFile(filesDir), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs);
            sb.AppendLine(r.ReadToEnd().TrimEnd());
        }
        catch
        {
            sb.AppendLine("(нет)");
        }

        sb.AppendLine();
        sb.AppendLine("=== logcat ===");
        var snap = LogcatSnapshot(filesDir);
        sb.AppendLine(File.Exists(snap) ? File.ReadAllText(snap) : ReadLogcat());

        var lines = sb.ToString().Split('\n');
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - maxLines)));
    }
}
