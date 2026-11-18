using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Org.Libsdl.App;

namespace Robust.Client.Android.Game;

/// <summary>
/// Hosts the engine. Runs in its own process (":game") because the engine is not restartable inside a process:
/// every connection gets a fresh process, and the launcher process is untouched.
/// SDL's Java side creates the surface and calls SDL_main (libmain.so) on its own thread, which jumps into
/// <see cref="GameHost.Main"/>.
/// </summary>
[Activity(Label = "@string/app_name", Theme = "@style/GameTheme", Process = ":game", Exported = false,
    LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation |
                           ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustNothing)]
public sealed class GameActivity : SDLActivity
{
    public const string ExtraSpecPath = "spec";
    public const string ExtraTouchControls = "touch";
    public const string ExtraTouchOpacity = "touch_opacity";
    public const string ExtraTouchScale = "touch_scale";

    public static string CrashFilePath(string filesDir) => Path.Combine(filesDir, "last-crash.txt");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var specPath = Intent?.GetStringExtra(ExtraSpecPath);
        GameHost.Prepare(specPath, FilesDir!.AbsolutePath);

        // Must happen before SDLActivity loads libmain.so and starts the SDL thread.
        GameHost.RegisterMain();

        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        HideSystemBars();

        if ((Intent?.GetBooleanExtra(ExtraTouchControls, true) ?? true) && MLayout != null)
        {
            var overlay = new TouchControls(this,
                Intent?.GetIntExtra(ExtraTouchOpacity, 60) ?? 60,
                Intent?.GetFloatExtra(ExtraTouchScale, 1f) ?? 1f);
            MLayout!.AddView(overlay, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        }
    }

    protected override string[] GetLibraries() => new[] { "SDL3", "main" };

    // SDL_main argv; the real configuration comes from the spec file.
    protected override string[] GetArguments() => Array.Empty<string>();

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            HideSystemBars();
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        // Android "back" behaves like Escape (close windows / open the game menu) instead of killing the game.
        if (e?.KeyCode == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Down)
                OnNativeKeyDown((int)Keycode.Escape);
            else if (e.Action == KeyEventActions.Up)
                OnNativeKeyUp((int)Keycode.Escape);
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // The engine cannot be started twice in one process; make sure the next game starts clean.
        global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
    }

    private void HideSystemBars()
    {
        if (Window == null)
            return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window.SetDecorFitsSystemWindows(false);
            var c = Window.InsetsController;
            c?.Hide(WindowInsets.Type.SystemBars());
            if (c != null)
                c.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
#pragma warning disable CA1422
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation |
                SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CA1422
        }
    }
}
