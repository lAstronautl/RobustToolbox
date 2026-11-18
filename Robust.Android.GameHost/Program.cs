using System.Text.Json;
using Robust.Launcher.Core;

namespace Robust.Android.GameHost;

/// <summary>
/// Game process entry point.
/// Usage: Robust.Android.GameHost &lt;spec.json&gt; [crash-file]
/// On Android this is started by the native host (libmain.so) through the Mono embedding API.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var crashFile = args.Length > 1 ? args[1] : null;
        try
        {
            if (args.Length < 1)
                throw new ArgumentException("Usage: Robust.Android.GameHost <spec.json> [crash-file]");

            var spec = JsonSerializer.Deserialize<GameLaunchSpec>(File.ReadAllText(args[0]))
                       ?? throw new InvalidDataException("Bad launch spec");
            // May contain the auth token; no reason to keep it on disk.
            File.Delete(args[0]);

            Console.WriteLine($"[GameHost] .NET {Environment.Version}, engine {spec.EngineDir} ({spec.EngineSource})");
            EngineLoader.Run(spec);
            Console.WriteLine("[GameHost] Engine exited");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[GameHost] Fatal: {e}");
            if (crashFile != null)
            {
                try
                {
                    File.WriteAllText(crashFile, $".NET {Environment.Version}\n{e}");
                }
                catch
                {
                    // ignored
                }
            }

            return 1;
        }
    }
}
