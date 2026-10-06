using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WinDeutsch.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly TimeProvider _time;

    public SettingsStore(string filePath, TimeProvider? time = null)
    {
        _filePath = filePath;
        _time = time ?? TimeProvider.System;
    }

    public bool LastLoadWasBroken { get; private set; }

    public string? BrokenFilePath { get; private set; }

    // Missing file -> defaults; unreadable JSON -> defaults and the file is renamed to "<name>.broken-<local time>".
    public AppSettings Load()
    {
        LastLoadWasBroken = false;
        BrokenFilePath = null;
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), Options)
                           ?? throw new JsonException("settings.json contains null");
            return Clamp(settings);
        }
        catch (JsonException)
        {
            var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            BrokenFilePath = $"{_filePath}.broken-{stamp}";
            File.Move(_filePath, BrokenFilePath, overwrite: true);
            LastLoadWasBroken = true;
            return new AppSettings();
        }
    }

    // Clamps `s` in place, then merges it into the existing file so keys this version does not know survive.
    public void Save(AppSettings s)
    {
        Clamp(s);
        var fresh = JsonSerializer.SerializeToNode(s, Options)!.AsObject();
        var target = ReadExistingObject() ?? new JsonObject();
        Merge(target, fresh);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, target.ToJsonString(Options));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private JsonObject? ReadExistingObject()
    {
        if (!File.Exists(_filePath))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceChild && target[key] is JsonObject targetChild)
                Merge(targetChild, sourceChild);
            else
                target[key] = value?.DeepClone();
        }
    }

    private static AppSettings Clamp(AppSettings s)
    {
        // A hand-edited file may carry explicit nulls for nested sections.
        s.Interval ??= new();
        s.Appearance ??= new();
        s.Widget ??= new();
        s.Popup ??= new();
        s.QuietHours ??= new();

        s.Interval.Seconds = Math.Clamp(s.Interval.Seconds, 10, 86400);
        s.Interval.MinSeconds = Math.Clamp(s.Interval.MinSeconds, 10, 86400);
        s.Interval.MaxSeconds = Math.Clamp(s.Interval.MaxSeconds, s.Interval.MinSeconds, 86400);
        s.PopupDurationSeconds = Math.Clamp(s.PopupDurationSeconds, 3, 120);
        s.TranslationDelaySeconds = Math.Clamp(s.TranslationDelaySeconds, 1, 60);
        s.QuizPercent = Math.Clamp(s.QuizPercent, 0, 100);
        s.Appearance.FontSize = Math.Clamp(s.Appearance.FontSize, 10, 48);
        s.Appearance.Opacity = Math.Clamp(s.Appearance.Opacity, 20, 100);
        return s;
    }
}
