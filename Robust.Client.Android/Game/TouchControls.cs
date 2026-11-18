using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Org.Libsdl.App;

namespace Robust.Client.Android.Game;

/// <summary>
/// On-screen controls drawn over the SDL surface. Everything is translated into ordinary keyboard and mouse
/// events through SDL's Java entry points, so the engine sees a normal keyboard + mouse and every keybind works.
/// Physical mice and keyboards bypass this overlay entirely.
/// </summary>
internal sealed class TouchControls : FrameLayout
{
    // android.view.MotionEvent constants used by SDLActivity.onNativeMouse.
    private const int ButtonPrimary = 1;
    private const int ButtonSecondary = 2;
    private const int ActionDown = 0;
    private const int ActionUp = 1;
    private const int ActionMove = 2;
    private const int ActionScroll = 8;

    private const long LongPressMs = 450;

    private readonly float _slop;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Vibrator? _vibrator;

    // Gesture state for the free (non-button) area.
    private int _pointerId = -1;
    private float _downX, _downY;
    private bool _dragging;
    private bool _longPressFired;
    private bool _scrolling;
    private float _scrollLastY;
    private Action? _longPressAction;

    private bool _rightClickMode;
    private ToggleKey? _rmbToggle;
    private readonly List<View> _hideable = new();
    private bool _hidden;

    public TouchControls(Context ctx, int opacityPercent, float scale) : base(ctx)
    {
        _slop = ViewConfiguration.Get(ctx)!.ScaledTouchSlop * 1.5f;
        _vibrator = (Vibrator?)ctx.GetSystemService(Context.VibratorService);
        var alpha = Math.Clamp(opacityPercent, 15, 100) / 100f;
        int Dp(float v) => (int)(v * scale * ctx.Resources!.DisplayMetrics!.Density);

        // --- Movement joystick (bottom left) ---
        var joy = new Joystick(ctx) { Alpha = alpha };
        var joyLp = new LayoutParams(Dp(170), Dp(170), GravityFlags.Bottom | GravityFlags.Left);
        joyLp.SetMargins(Dp(24), 0, 0, Dp(24));
        AddView(joy, joyLp);
        _hideable.Add(joy);

        // --- Action buttons (bottom right), keys are the default SS14 binds ---
        var grid = new GridLayout(ctx) { ColumnCount = 3, Alpha = alpha };
        void Key(string label, Keycode key, GridLayout parent)
        {
            var b = new KeyButton(ctx, label, key);
            var lp = new GridLayout.LayoutParams { Width = Dp(64), Height = Dp(64) };
            lp.SetMargins(Dp(4), Dp(4), Dp(4), Dp(4));
            parent.AddView(b, lp);
        }

        Key("Исп.", Keycode.Z, grid); // ActivateItemInHand
        Key("Акт.", Keycode.E, grid); // ActivateItemInWorld
        Key("Рука", Keycode.X, grid); // SwapHands
        Key("Брос.", Keycode.Q, grid); // Drop
        Key("Отпуст.", Keycode.H, grid); // ReleasePulledObject
        Key("Инв.", Keycode.I, grid); // OpenInventoryMenu
        var gridLp = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom | GravityFlags.Right);
        gridLp.SetMargins(0, 0, Dp(16), Dp(16));
        AddView(grid, gridLp);
        _hideable.Add(grid);

        // --- Modifiers and utility keys (right edge, above the action grid) ---
        var side = new LinearLayout(ctx) { Orientation = Orientation.Vertical, Alpha = alpha };
        void Side(View v)
        {
            var lp = new LinearLayout.LayoutParams(Dp(64), Dp(48));
            lp.SetMargins(Dp(4), Dp(4), Dp(4), Dp(4));
            side.AddView(v, lp);
        }

        _rmbToggle = new ToggleKey(ctx, "ПКМ", null, on => _rightClickMode = on);
        Side(_rmbToggle);
        Side(new ToggleKey(ctx, "Shift", Keycode.ShiftLeft)); // + tap = examine
        Side(new ToggleKey(ctx, "Ctrl", Keycode.CtrlLeft)); // + tap = pull
        Side(new ToggleKey(ctx, "Alt", Keycode.AltLeft)); // + tap = alt-activate
        var sideLp = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Right | GravityFlags.CenterVertical);
        sideLp.SetMargins(0, 0, Dp(16), Dp(90));
        AddView(side, sideLp);
        _hideable.Add(side);

        // --- Top bar: menu, chat, zoom, hide ---
        var topBar = new LinearLayout(ctx) { Orientation = Orientation.Horizontal, Alpha = alpha };
        void Top(View v)
        {
            var lp = new LinearLayout.LayoutParams(Dp(56), Dp(44));
            lp.SetMargins(Dp(3), Dp(3), Dp(3), Dp(3));
            topBar.AddView(v, lp);
        }

        Top(new KeyButton(ctx, "Esc", Keycode.Escape));
        Top(new KeyButton(ctx, "Чат", Keycode.T)); // FocusChat
        Top(new KeyButton(ctx, "Tab", Keycode.Tab)); // cycle chat channel
        Top(new KeyButton(ctx, "−", Keycode.Numpad4)); // ZoomOut
        Top(new KeyButton(ctx, "+", Keycode.Numpad6)); // ZoomIn
        var topLp = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Top | GravityFlags.Left);
        topLp.SetMargins(Dp(8), Dp(8), 0, 0);
        AddView(topBar, topLp);
        _hideable.Add(topBar);

        // Always-visible toggle to hide the controls (e.g. when using a physical keyboard + mouse).
        var hide = new KeyButton(ctx, "◐", null, () =>
        {
            _hidden = !_hidden;
            foreach (var v in _hideable)
                v.Visibility = _hidden ? ViewStates.Gone : ViewStates.Visible;
        }) { Alpha = alpha };
        var hideLp = new LayoutParams(Dp(44), Dp(44), GravityFlags.Top | GravityFlags.Right);
        hideLp.SetMargins(0, Dp(8), Dp(8), 0);
        AddView(hide, hideLp);
    }

    private void Vibrate()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                _vibrator?.Vibrate(VibrationEffect.CreateOneShot(20, VibrationEffect.DefaultAmplitude));
        }
        catch
        {
            // No permission or no vibrator; not important.
        }
    }

    // ------------------------------------------------------------------ free-area gestures

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        // Real mice / styluses go straight to SDL.
        if (e == null || e.IsFromSource(InputSourceType.Mouse) || e.GetToolType(0) == MotionEventToolType.Mouse)
            return false;

        // Let buttons/joystick handle touches that start on them.
        if (base.DispatchTouchEvent(e))
            return true;

        return HandleFreeTouch(e);
    }

    private bool HandleFreeTouch(MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            {
                _pointerId = e.GetPointerId(0);
                _downX = e.GetX();
                _downY = e.GetY();
                _dragging = false;
                _longPressFired = false;
                _scrolling = false;
                Mouse(0, ActionMove, _downX, _downY);

                var x = _downX;
                var y = _downY;
                _longPressAction = () =>
                {
                    if (_dragging || _scrolling || _pointerId < 0)
                        return;
                    _longPressFired = true;
                    Vibrate();
                    SendClick(ButtonSecondary, x, y);
                };
                _handler.PostDelayed(_longPressAction, LongPressMs);
                return true;
            }
            case MotionEventActions.PointerDown:
            {
                // Second finger: two-finger drag scrolls (lists, chat, etc.).
                CancelPendingLongPress();
                if (_dragging)
                {
                    Mouse(ButtonPrimary, ActionUp, e.GetX(0), e.GetY(0));
                    _dragging = false;
                }

                _scrolling = true;
                _scrollLastY = AverageY(e);
                return true;
            }
            case MotionEventActions.Move:
            {
                if (_scrolling)
                {
                    var y = AverageY(e);
                    var step = 40 * Resources!.DisplayMetrics!.Density;
                    while (Math.Abs(y - _scrollLastY) >= step)
                    {
                        var dir = y > _scrollLastY ? 1f : -1f;
                        Mouse(0, ActionScroll, 0, dir);
                        _scrollLastY += dir * step;
                    }

                    return true;
                }

                var idx = e.FindPointerIndex(_pointerId);
                if (idx < 0)
                    return true;
                var mx = e.GetX(idx);
                var my = e.GetY(idx);

                if (!_dragging && !_longPressFired && Math.Abs(mx - _downX) + Math.Abs(my - _downY) > _slop)
                {
                    // Drag with the left button held: moving items, selecting, dragging windows.
                    CancelPendingLongPress();
                    _dragging = true;
                    Mouse(ButtonPrimary, ActionDown, _downX, _downY);
                }

                Mouse(0, ActionMove, mx, my);
                return true;
            }
            case MotionEventActions.Up:
            {
                CancelPendingLongPress();
                var x = e.GetX();
                var y = e.GetY();
                if (_scrolling)
                {
                    // nothing
                }
                else if (_dragging)
                {
                    Mouse(ButtonPrimary, ActionUp, x, y);
                }
                else if (!_longPressFired)
                {
                    SendClick(_rightClickMode ? ButtonSecondary : ButtonPrimary, x, y);
                    if (_rightClickMode)
                        _rmbToggle?.SetOn(false);
                }

                _pointerId = -1;
                _dragging = false;
                _scrolling = false;
                return true;
            }
            case MotionEventActions.Cancel:
            {
                CancelPendingLongPress();
                if (_dragging)
                    Mouse(ButtonPrimary, ActionUp, e.GetX(), e.GetY());
                _pointerId = -1;
                _dragging = false;
                _scrolling = false;
                return true;
            }
        }

        return true;
    }

    private static float AverageY(MotionEvent e)
    {
        float sum = 0;
        for (var i = 0; i < e.PointerCount; i++)
            sum += e.GetY(i);
        return sum / e.PointerCount;
    }

    private void CancelPendingLongPress()
    {
        if (_longPressAction != null)
            _handler.RemoveCallbacks(_longPressAction);
        _longPressAction = null;
    }

    private static void SendClick(int button, float x, float y)
    {
        Mouse(0, ActionMove, x, y);
        Mouse(button, ActionDown, x, y);
        Mouse(button, ActionUp, x, y);
    }

    private static void Mouse(int button, int action, float x, float y) =>
        SDLActivity.OnNativeMouse(button, action, x, y, false);

    // ------------------------------------------------------------------ widgets

    private static void Key(Keycode key, bool down)
    {
        if (down)
            SDLActivity.OnNativeKeyDown((int)key);
        else
            SDLActivity.OnNativeKeyUp((int)key);
    }

    /// <summary>Press-and-hold key (or a plain action button).</summary>
    private sealed class KeyButton : TextView
    {
        private readonly Keycode? _key;
        private readonly Action? _onTap;

        public KeyButton(Context ctx, string label, Keycode? key, Action? onTap = null) : base(ctx)
        {
            _key = key;
            _onTap = onTap;
            Text = label;
            Gravity = GravityFlags.Center;
            SetTextColor(Color.White);
            SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13);
            SetBackground(false);
        }

        private void SetBackground(bool pressed)
        {
            var d = new global::Android.Graphics.Drawables.GradientDrawable();
            d.SetColor(pressed ? Color.Argb(200, 79, 195, 247) : Color.Argb(150, 20, 28, 40));
            d.SetStroke(2, Color.Argb(180, 140, 160, 190));
            d.SetCornerRadius(18);
            Background = d;
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            switch (e?.ActionMasked)
            {
                case MotionEventActions.Down:
                    SetBackground(true);
                    if (_key is { } k) Key(k, true);
                    return true;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    SetBackground(false);
                    if (_key is { } k2) Key(k2, false);
                    if (e.ActionMasked == MotionEventActions.Up) _onTap?.Invoke();
                    return true;
            }

            return true;
        }
    }

    /// <summary>Sticky modifier: tap to hold the key down, tap again to release.</summary>
    private sealed class ToggleKey : TextView
    {
        private readonly Keycode? _key;
        private readonly Action<bool>? _changed;
        private bool _on;

        public ToggleKey(Context ctx, string label, Keycode? key, Action<bool>? changed = null) : base(ctx)
        {
            _key = key;
            _changed = changed;
            Text = label;
            Gravity = GravityFlags.Center;
            SetTextColor(Color.White);
            SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13);
            Redraw();
        }

        public void SetOn(bool on)
        {
            if (_on == on) return;
            _on = on;
            if (_key is { } k) Key(k, on);
            _changed?.Invoke(on);
            Redraw();
        }

        private void Redraw()
        {
            var d = new global::Android.Graphics.Drawables.GradientDrawable();
            d.SetColor(_on ? Color.Argb(220, 255, 183, 77) : Color.Argb(150, 20, 28, 40));
            d.SetStroke(2, Color.Argb(180, 140, 160, 190));
            d.SetCornerRadius(18);
            Background = d;
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e?.ActionMasked == MotionEventActions.Up)
                SetOn(!_on);
            return true;
        }
    }

    /// <summary>8-way movement stick mapped to W/A/S/D.</summary>
    private sealed class Joystick : View
    {
        private static readonly Keycode[] Keys = { Keycode.W, Keycode.A, Keycode.S, Keycode.D };
        private readonly bool[] _held = new bool[4];
        private readonly Paint _base = new() { AntiAlias = true, Color = Color.Argb(110, 20, 28, 40) };
        private readonly Paint _ring = new() { AntiAlias = true, Color = Color.Argb(170, 140, 160, 190), StrokeWidth = 4 };
        private readonly Paint _knob = new() { AntiAlias = true, Color = Color.Argb(200, 79, 195, 247) };
        private float _kx, _ky;
        private bool _active;

        public Joystick(Context ctx) : base(ctx)
        {
            _ring.SetStyle(Paint.Style.Stroke);
        }

        protected override void OnDraw(Canvas canvas)
        {
            var cx = Width / 2f;
            var cy = Height / 2f;
            var r = Math.Min(cx, cy) - 4;
            canvas.DrawCircle(cx, cy, r, _base);
            canvas.DrawCircle(cx, cy, r, _ring);
            canvas.DrawCircle(_active ? _kx : cx, _active ? _ky : cy, r * 0.38f, _knob);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null) return false;
            var cx = Width / 2f;
            var cy = Height / 2f;
            var r = Math.Min(cx, cy);

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.Move:
                {
                    var dx = e.GetX() - cx;
                    var dy = e.GetY() - cy;
                    var len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len > r)
                    {
                        dx = dx / len * r;
                        dy = dy / len * r;
                    }

                    _active = true;
                    _kx = cx + dx;
                    _ky = cy + dy;

                    var want = new bool[4];
                    if (len > r * 0.3f)
                    {
                        // 8 sectors of 45°: each direction key covers 135° so diagonals press two keys.
                        var angle = MathF.Atan2(-dy, dx) * 180 / MathF.PI; // 0 = right, 90 = up
                        want[0] = angle is > 22.5f and < 157.5f; // up
                        want[2] = angle is < -22.5f and > -157.5f; // down
                        want[1] = angle is > 112.5f or < -112.5f; // left
                        want[3] = angle is > -67.5f and < 67.5f; // right
                    }

                    Apply(want);
                    Invalidate();
                    return true;
                }
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _active = false;
                    Apply(new bool[4]);
                    Invalidate();
                    return true;
            }

            return true;
        }

        private void Apply(bool[] want)
        {
            for (var i = 0; i < 4; i++)
            {
                if (want[i] == _held[i]) continue;
                _held[i] = want[i];
                Key(Keys[i], want[i]);
            }
        }
    }
}
