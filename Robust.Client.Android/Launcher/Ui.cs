using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Robust.Client.Android.Launcher;

/// <summary>Small helpers for building the launcher UI in code.</summary>
internal static class Ui
{
    public static readonly Color Bg = Color.ParseColor("#11161F");
    public static readonly Color Panel = Color.ParseColor("#1B2433");
    public static readonly Color PanelPressed = Color.ParseColor("#26344A");
    public static readonly Color Accent = Color.ParseColor("#4FC3F7");
    public static readonly Color Text = Color.ParseColor("#E8EEF6");
    public static readonly Color Muted = Color.ParseColor("#8A99AD");
    public static readonly Color Good = Color.ParseColor("#66BB6A");
    public static readonly Color Bad = Color.ParseColor("#EF5350");

    public static int Dp(Context ctx, float dp) => (int)(dp * ctx.Resources!.DisplayMetrics!.Density + 0.5f);

    public static Drawable Rounded(Context ctx, Color color, float radiusDp = 10)
    {
        var d = new GradientDrawable();
        d.SetColor(color);
        d.SetCornerRadius(Dp(ctx, radiusDp));
        return d;
    }

    public static TextView Label(Context ctx, string text, float sp = 15, Color? color = null, bool bold = false)
    {
        var t = new TextView(ctx) { Text = text };
        t.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
        t.SetTextColor(color ?? Text);
        if (bold) t.SetTypeface(null, TypefaceStyle.Bold);
        return t;
    }

    public static Button Button(Context ctx, string text, Action onClick, bool primary = false)
    {
        var b = new Button(ctx) { Text = text };
        b.SetAllCaps(false);
        b.SetTextColor(primary ? Bg : Text);
        b.Background = Rounded(ctx, primary ? Accent : Panel);
        b.SetPadding(Dp(ctx, 16), Dp(ctx, 10), Dp(ctx, 16), Dp(ctx, 10));
        b.Click += (_, _) => onClick();
        return b;
    }

    public static EditText Input(Context ctx, string hint, string value = "", bool password = false)
    {
        var e = new EditText(ctx) { Hint = hint, Text = value };
        e.SetTextColor(Text);
        e.SetHintTextColor(Muted);
        e.SetSingleLine(true);
        e.Background = Rounded(ctx, Panel, 8);
        e.SetPadding(Dp(ctx, 12), Dp(ctx, 10), Dp(ctx, 12), Dp(ctx, 10));
        if (password)
            e.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationPassword;
        return e;
    }

    public static LinearLayout Column(Context ctx, int paddingDp = 0)
    {
        var l = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        var p = Dp(ctx, paddingDp);
        l.SetPadding(p, p, p, p);
        return l;
    }

    public static LinearLayout Row(Context ctx)
    {
        var l = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        l.SetGravity(GravityFlags.CenterVertical);
        return l;
    }

    public static LinearLayout.LayoutParams Fill(float weight = 0, int topMarginDp = 0, Context? ctx = null)
    {
        var lp = new LinearLayout.LayoutParams(
            weight > 0 ? 0 : ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent, weight);
        if (ctx != null) lp.TopMargin = Dp(ctx, topMarginDp);
        return lp;
    }

    public static LinearLayout.LayoutParams Wrap(int leftMarginDp = 0, Context? ctx = null)
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        if (ctx != null) lp.LeftMargin = Dp(ctx, leftMarginDp);
        return lp;
    }
}
