using Microsoft.Win32;

namespace WinDeutsch.App.Platform;

// Time since the last keyboard/mouse input, and whether the session is locked.
internal sealed class IdleDetector : IDisposable
{
    public IdleDetector() => SystemEvents.SessionSwitch += OnSessionSwitch;

    public bool IsSessionLocked { get; private set; }

    public static TimeSpan IdleTime
    {
        get
        {
            var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
            if (!NativeMethods.GetLastInputInfo(ref info))
                return TimeSpan.Zero;
            // Both values are 32-bit tick counts; unsigned subtraction survives the 49-day wrap.
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            IsSessionLocked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            IsSessionLocked = false;
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
