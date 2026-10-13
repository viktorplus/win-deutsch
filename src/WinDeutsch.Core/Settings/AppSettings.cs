using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;

namespace WinDeutsch.Core.Settings;

// Defaults are the first-run values.
public sealed class AppSettings
{
    public DisplayMode Mode { get; set; } = DisplayMode.Widget;
    public IntervalSettings Interval { get; set; } = new();
    public int PopupDurationSeconds { get; set; } = 10;             // 3..120
    public TranslationMode WidgetTranslation { get; set; } = TranslationMode.Delayed;
    public TranslationMode PopupTranslation { get; set; } = TranslationMode.Immediate;
    public int TranslationDelaySeconds { get; set; } = 4;           // 1..60
    public bool QuizEnabled { get; set; } = true;
    public int QuizPercent { get; set; } = 20;                      // 0..100
    public AppearanceSettings Appearance { get; set; } = new();
    public WindowPlacement Widget { get; set; } = new();
    public PopupPlacement Popup { get; set; } = new();
    public bool HideOnFullscreen { get; set; } = true;
    public QuietHoursSettings QuietHours { get; set; } = new();
    public bool PauseWhenIdle { get; set; } = true;
    public int IdleMinutes { get; set; } = 5;
    public bool HotkeysEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public DateTimeOffset? PausedUntil { get; set; }
    public bool WidgetHidden { get; set; } = false;
    // Dictionary files already connected at startup, relative to the dictionaries folder (DictionaryFolder.ImportNew).
    public List<string> ImportedDictionaries { get; set; } = [];
}

public sealed class IntervalSettings
{
    public IntervalKind Kind { get; set; } = IntervalKind.Fixed;
    public int Seconds { get; set; } = 60;                          // 10..86400
    public int MinSeconds { get; set; } = 60;
    public int MaxSeconds { get; set; } = 300;
}

public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "Dark";                     // "Light" | "Dark" | "Custom"
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 18;                         // 10..48
    public string TextColor { get; set; } = "#F2F2F2";
    public string BackgroundColor { get; set; } = "#202020";
    public int Opacity { get; set; } = 85;                          // 20..100
    public bool TopMost { get; set; } = true;
    public bool ControlsOnHover { get; set; } = true;
    public bool LockPosition { get; set; } = false;
}

// X, Y null = default corner.
public sealed class WindowPlacement
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 90;
}

public enum PopupCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public enum PopupMonitor { Cursor, Primary }

public sealed class PopupPlacement
{
    public PopupCorner Corner { get; set; } = PopupCorner.BottomRight;
    public PopupMonitor Monitor { get; set; } = PopupMonitor.Cursor;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 120;
}

public sealed class QuietHoursSettings
{
    public bool Enabled { get; set; } = false;
    public TimeOnly From { get; set; } = new(22, 0);
    public TimeOnly To { get; set; } = new(8, 0);
}
