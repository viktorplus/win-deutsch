namespace WinDeutsch.App.Platform;

internal static class FullscreenDetector
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    // True when the shell reports a "do not disturb" state, or the foreground window (not the desktop/shell,
    // not ours) covers its whole monitor.
    public static bool IsFullscreenAppActive()
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) == 0 &&
            state is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN or NativeMethods.QUNS_PRESENTATION_MODE)
            return true;

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0 || hwnd == NativeMethods.GetDesktopWindow() || hwnd == NativeMethods.GetShellWindow())
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId)
            return false;

        var buffer = new char[64];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        if (ShellClasses.Contains(new string(buffer, 0, length)))
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }
}
