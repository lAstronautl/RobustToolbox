using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Robust.Launcher.Core;

namespace Robust.Client.Android.Game;

/// <summary>
/// Bridges SDL's <c>SDL_main</c> (called by SDLActivity on the SDL thread) to the downloaded engine.
/// </summary>
internal static unsafe class GameHost
{
    private const string Tag = "RobustGame";

    private static GameLaunchSpec? _spec;
    private static string _filesDir = "";
    private static string? _prepareError;

    [DllImport("main")]
    private static extern void robust_set_main(delegate* unmanaged[Cdecl]<int, nint, int> fn);

    public static void Prepare(string? specPath, string filesDir)
    {
        _filesDir = filesDir;
        try
        {
            if (specPath == null || !File.Exists(specPath))
                throw new InvalidOperationException("Нет параметров запуска. Запустите игру из лаунчера.");

            _spec = JsonSerializer.Deserialize<GameLaunchSpec>(File.ReadAllText(specPath));
            // Contains the auth token; no reason to keep it on disk.
            File.Delete(specPath);
        }
        catch (Exception e)
        {
            _prepareError = e.Message;
        }
    }

    public static void RegisterMain()
    {
        robust_set_main(&Main);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Main(int argc, nint argv)
    {
        try
        {
            if (_spec == null)
                throw new InvalidOperationException(_prepareError ?? "Нет параметров запуска.");

            // Official engine builds put user data under $HOME/.local/share (their Linux code path).
            Environment.SetEnvironmentVariable("HOME", _filesDir);

            // SDL hints can be given through the environment.
            Environment.SetEnvironmentVariable("SDL_ANDROID_TRAP_BACK_BUTTON", "1");
            // Touches still reach SDL as mouse events when the overlay passes them through.
            Environment.SetEnvironmentVariable("SDL_TOUCH_MOUSE_EVENTS", "1");

            global::Android.Util.Log.Info(Tag, $"Starting engine from {_spec.EngineDir}");
            EngineLoader.Run(_spec);
            global::Android.Util.Log.Info(Tag, "Engine exited");
            return 0;
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error(Tag, e.ToString());
            try
            {
                File.WriteAllText(GameActivity.CrashFilePath(_filesDir), e.ToString());
            }
            catch
            {
                // ignored
            }

            return 1;
        }
    }
}
