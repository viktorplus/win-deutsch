using System.Text.Json;
using System.Text.Json.Nodes;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsStore NewStore() => new(_file, _time);

    // Structural snapshot independent of the store's own serializer options.
    private static string Snapshot(AppSettings s) => JsonSerializer.Serialize(s);

    [Fact]
    public void Load_MissingFile_ReturnsFirstRunDefaults()
    {
        var store = NewStore();

        var s = store.Load();

        Assert.False(store.LastLoadWasBroken);
        Assert.Equal(DisplayMode.Widget, s.Mode);
        Assert.Equal((IntervalKind.Fixed, 60, 60, 300), (s.Interval.Kind, s.Interval.Seconds, s.Interval.MinSeconds, s.Interval.MaxSeconds));
        Assert.Equal(10, s.PopupDurationSeconds);
        Assert.Equal((TranslationMode.Delayed, TranslationMode.Immediate, 4), (s.WidgetTranslation, s.PopupTranslation, s.TranslationDelaySeconds));
        Assert.Equal((true, 20), (s.QuizEnabled, s.QuizPercent));
        Assert.Equal(("Dark", "Segoe UI", 18, "#F2F2F2", "#202020", 85), (s.Appearance.Theme, s.Appearance.FontFamily, s.Appearance.FontSize, s.Appearance.TextColor, s.Appearance.BackgroundColor, s.Appearance.Opacity));
        Assert.Equal((true, true, false), (s.Appearance.TopMost, s.Appearance.ControlsOnHover, s.Appearance.LockPosition));
        Assert.Equal(((double?)null, (double?)null, 360d, 90d), (s.Widget.X, s.Widget.Y, s.Widget.Width, s.Widget.Height));
        Assert.Equal((PopupCorner.BottomRight, PopupMonitor.Cursor, 380d, 120d), (s.Popup.Corner, s.Popup.Monitor, s.Popup.Width, s.Popup.Height));
        Assert.Equal((false, new TimeOnly(22, 0), new TimeOnly(8, 0)), (s.QuietHours.Enabled, s.QuietHours.From, s.QuietHours.To));
        Assert.Equal((true, true, 5, true, false), (s.HideOnFullscreen, s.PauseWhenIdle, s.IdleMinutes, s.HotkeysEnabled, s.StartWithWindows));
        Assert.Null(s.PausedUntil);
        Assert.False(s.WidgetHidden);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaultsAndRenamesFileWithTimestamp()
    {
        File.WriteAllText(_file, "{ \"mode\": \"Popup\", oops");
        var store = NewStore();

        var s = store.Load();

        var expectedBroken = Path.Combine(_dir, "settings.json.broken-20261005-153000");
        Assert.True(store.LastLoadWasBroken);
        Assert.Equal(expectedBroken, store.BrokenFilePath);
        Assert.False(File.Exists(_file));
        Assert.Equal("{ \"mode\": \"Popup\", oops", File.ReadAllText(expectedBroken));
        Assert.Equal(Snapshot(new AppSettings()), Snapshot(s));
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSameValues()
    {
        var s = new AppSettings
        {
            Mode = DisplayMode.Popup,
            Interval = new IntervalSettings { Kind = IntervalKind.Random, Seconds = 120, MinSeconds = 90, MaxSeconds = 600 },
            PopupDurationSeconds = 15,
            WidgetTranslation = TranslationMode.OnClick,
            PopupTranslation = TranslationMode.Never,
            TranslationDelaySeconds = 7,
            QuizEnabled = false,
            QuizPercent = 35,
            Appearance = new AppearanceSettings { Theme = "Custom", FontFamily = "Arial", FontSize = 22, TextColor = "#000000", BackgroundColor = "#FFFFFF", Opacity = 60, TopMost = false, ControlsOnHover = false, LockPosition = true },
            Widget = new WindowPlacement { X = -1200.5, Y = 300, Width = 400, Height = 100 },
            Popup = new PopupPlacement { Corner = PopupCorner.TopLeft, Monitor = PopupMonitor.Primary, Width = 420, Height = 140 },
            HideOnFullscreen = false,
            QuietHours = new QuietHoursSettings { Enabled = true, From = new TimeOnly(23, 15), To = new TimeOnly(7, 45) },
            PauseWhenIdle = false,
            IdleMinutes = 12,
            HotkeysEnabled = false,
            StartWithWindows = true,
            PausedUntil = new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.FromHours(2)),
            WidgetHidden = true,
        };
        var expected = Snapshot(s);

        NewStore().Save(s);
        var loaded = NewStore().Load();

        Assert.Equal(expected, Snapshot(loaded));
    }

    [Fact]
    public void Save_PreservesUnknownKeys_AndWritesCamelCaseEnumStrings()
    {
        File.WriteAllText(_file, """
            { "futureKey": { "a": 1 }, "mode": "Widget",
              "appearance": { "futureNested": "x", "fontSize": 20 } }
            """);
        var store = NewStore();
        var s = store.Load();
        Assert.Equal(20, s.Appearance.FontSize);
        s.Mode = DisplayMode.Popup;
        s.Appearance.FontSize = 24;

        store.Save(s);

        var json = JsonNode.Parse(File.ReadAllText(_file))!;
        Assert.Equal(1, (int)json["futureKey"]!["a"]!);
        Assert.Equal("x", (string)json["appearance"]!["futureNested"]!);
        Assert.Equal(24, (int)json["appearance"]!["fontSize"]!);
        Assert.Equal("Popup", (string)json["mode"]!);
        Assert.Equal("Fixed", (string)json["interval"]!["kind"]!);
    }

    [Fact]
    public void Save_ClampsValuesToAllowedRanges()
    {
        var s = new AppSettings
        {
            PopupDurationSeconds = 500,
            TranslationDelaySeconds = 0,
            QuizPercent = -5,
            Interval = new IntervalSettings { Seconds = 1, MinSeconds = 5, MaxSeconds = 100000 },
            Appearance = new AppearanceSettings { FontSize = 100, Opacity = 5 },
        };

        NewStore().Save(s);
        var loaded = NewStore().Load();

        Assert.Equal((120, 1, 0), (loaded.PopupDurationSeconds, loaded.TranslationDelaySeconds, loaded.QuizPercent));
        Assert.Equal((10, 10, 86400), (loaded.Interval.Seconds, loaded.Interval.MinSeconds, loaded.Interval.MaxSeconds));
        Assert.Equal((48, 20), (loaded.Appearance.FontSize, loaded.Appearance.Opacity));
    }
}
