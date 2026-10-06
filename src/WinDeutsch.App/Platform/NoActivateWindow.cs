using System.Windows;
using System.Windows.Interop;

namespace WinDeutsch.App.Platform;

internal static class NoActivateWindow
{
    // Never takes keyboard focus and stays off the taskbar/Alt+Tab.
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            (nint)((long)style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW));
    }
}
