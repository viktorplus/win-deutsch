using System.Windows.Interop;

namespace WinDeutsch.App.Platform;

// Global hotkeys Ctrl+Alt+<key> registered on a hidden message-only window.
internal sealed class HotkeyManager : IDisposable
{
    private const nint HwndMessage = -3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly List<string> _failed = new();

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("WinDeutsch.Hotkeys") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public IReadOnlyList<string> FailedHotkeys => _failed;

    // Replaces every registration; keys a different program already holds go to FailedHotkeys.
    public void Register(IReadOnlyList<(string Name, uint VirtualKey, Action Action)> hotkeys)
    {
        UnregisterAll();
        var id = 1;
        foreach (var (name, vk, action) in hotkeys)
        {
            if (NativeMethods.RegisterHotKey(_source.Handle, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, vk))
                _actions[id] = action;
            else
                _failed.Add(name);
            id++;
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _failed.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            handled = true;
            action();
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
