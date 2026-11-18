package robust.android;

import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;

import org.libsdl.app.SDLActivity;

import java.io.File;

/**
 * Hosts the engine in its own process (":game"). Deliberately plain Java: this process must not start the
 * app's own .NET runtime, because the native host (libmain.so, robust_host.c) starts the Mono runtime of the
 * .NET version the engine was built for.
 */
public class GameActivity extends SDLActivity {
    public static final String EXTRA_RUNTIME_DIR = "runtime_dir";
    public static final String EXTRA_HOST_DLL = "host_dll";
    public static final String EXTRA_SPEC = "spec";
    public static final String EXTRA_CRASH = "crash";
    public static final String EXTRA_RUNTIME_CONFIG = "runtime_config";
    public static final String EXTRA_TOUCH = "touch";
    public static final String EXTRA_TOUCH_OPACITY = "touch_opacity";
    public static final String EXTRA_TOUCH_SCALE = "touch_scale";

    private static final String TAG = "RobustGame";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Intent intent = getIntent();
        String runtimeDir = intent.getStringExtra(EXTRA_RUNTIME_DIR);

        // .NET's crypto (TLS for account login) is a JNI library; load it from Java so JNI_OnLoad runs
        // with this app's class loader, which contains the net.dot.android.crypto classes.
        if (runtimeDir != null) {
            File crypto = new File(runtimeDir, "native/libSystem.Security.Cryptography.Native.Android.so");
            if (crypto.exists()) {
                try {
                    System.load(crypto.getAbsolutePath());
                } catch (Throwable t) {
                    Log.w(TAG, "Failed to load .NET crypto library", t);
                }
            }
        }

        super.onCreate(savedInstanceState);

        Window window = getWindow();
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        hideSystemBars();

        if (intent.getBooleanExtra(EXTRA_TOUCH, true) && mLayout != null) {
            TouchControls overlay = new TouchControls(this,
                    intent.getIntExtra(EXTRA_TOUCH_OPACITY, 60),
                    intent.getFloatExtra(EXTRA_TOUCH_SCALE, 1f));
            mLayout.addView(overlay, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        }
    }

    @Override
    protected String[] getLibraries() {
        return new String[]{"SDL3", "main"};
    }

    @Override
    protected String[] getArguments() {
        Intent intent = getIntent();
        return new String[]{
                nonNull(intent.getStringExtra(EXTRA_RUNTIME_DIR)),
                nonNull(intent.getStringExtra(EXTRA_HOST_DLL)),
                nonNull(intent.getStringExtra(EXTRA_SPEC)),
                nonNull(intent.getStringExtra(EXTRA_CRASH)),
                getApplicationInfo().nativeLibraryDir,
                nonNull(intent.getStringExtra(EXTRA_RUNTIME_CONFIG)),
        };
    }

    private static String nonNull(String s) {
        return s == null ? "" : s;
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus)
            hideSystemBars();
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        // Android "back" behaves like Escape (close windows / game menu) instead of killing the game.
        if (e.getKeyCode() == KeyEvent.KEYCODE_BACK) {
            if (e.getAction() == KeyEvent.ACTION_DOWN)
                onNativeKeyDown(KeyEvent.KEYCODE_ESCAPE);
            else if (e.getAction() == KeyEvent.ACTION_UP)
                onNativeKeyUp(KeyEvent.KEYCODE_ESCAPE);
            return true;
        }
        return super.dispatchKeyEvent(e);
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        // The .NET runtime and the engine cannot be restarted in one process: always start clean.
        android.os.Process.killProcess(android.os.Process.myPid());
    }

    @SuppressWarnings("deprecation")
    private void hideSystemBars() {
        Window window = getWindow();
        if (window == null)
            return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false);
            WindowInsetsController c = window.getInsetsController();
            if (c != null) {
                c.hide(WindowInsets.Type.systemBars());
                c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            window.getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        }
    }
}
