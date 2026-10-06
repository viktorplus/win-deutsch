using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WinDeutsch.App.Platform;

// Monitor work areas in WPF device-independent units (the app is system-DPI aware: one scale for all monitors).
internal static class ScreenHelper
{
    private const double Margin = 12;

    public static event EventHandler? DisplaySettingsChanged;

    static ScreenHelper() =>
        SystemEvents.DisplaySettingsChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(() => DisplaySettingsChanged?.Invoke(null, EventArgs.Empty));

    private static double DipPerPixel
    {
        get
        {
            var px = Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0;
            return px > 0 ? SystemParameters.PrimaryScreenWidth / px : 1;
        }
    }

    public static Rect ToDip(System.Drawing.Rectangle px)
    {
        var k = DipPerPixel;
        return new Rect(px.X * k, px.Y * k, px.Width * k, px.Height * k);
    }

    public static Rect PrimaryWorkArea => ToDip((Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).WorkingArea);

    public static Rect CursorWorkArea => ToDip(Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea);

    // Top-left of a window of `size` placed in `corner` of `area`.
    public static Point CornerPosition(Rect area, Size size, bool right, bool bottom) => new(
        right ? area.Right - size.Width - Margin : area.Left + Margin,
        bottom ? area.Bottom - size.Height - Margin : area.Top + Margin);

    // `rect` unchanged while a reasonable part of it is on some monitor; otherwise the bottom-right corner of the primary.
    public static Rect EnsureOnScreen(Rect rect)
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var visible = Rect.Intersect(rect, ToDip(screen.WorkingArea));
            if (!visible.IsEmpty && visible.Width >= Math.Min(40, rect.Width) && visible.Height >= Math.Min(20, rect.Height))
                return rect;
        }
        var corner = CornerPosition(PrimaryWorkArea, rect.Size, right: true, bottom: true);
        return new Rect(corner, rect.Size);
    }
}
