using System.Globalization;

namespace WinDeutsch.App.Windows.Settings;

// Parsing and range checks for the text fields of the settings window. An error message means "do not save".
public static class SettingsInput
{
    public static string? TryInt(string text, int min, int max, out int value)
    {
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
            return null;
        value = 0;
        return $"Введите целое число от {min} до {max}";
    }

    // Interval typed in `unit` seconds per step (1 = seconds, 60 = minutes); the result must be 10 s .. 24 h.
    public static string? TryIntervalSeconds(string text, int unit, out int seconds)
    {
        seconds = 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1_000_000)
            return "Введите целое число";
        seconds = v * unit;
        return seconds is >= 10 and <= 86400 ? null : "Допустимо от 10 секунд до 24 часов";
    }

    // "#RRGGBB" with or without '#'; returns the normalized upper-case form.
    public static string? TryHex(string text, out string value)
    {
        var s = text.Trim().TrimStart('#');
        if (s.Length == 6 && s.All(Uri.IsHexDigit))
        {
            value = "#" + s.ToUpperInvariant();
            return null;
        }
        value = "";
        return "Цвет в формате #RRGGBB";
    }

    public static string? TryTime(string text, out TimeOnly value)
    {
        if (TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return null;
        return "Время в формате ЧЧ:ММ";
    }

    // Empty text = not set (null); otherwise a screen coordinate.
    public static string? TryCoordinate(string text, out double? value)
    {
        value = null;
        var s = text.Trim().Replace(',', '.');
        if (s.Length == 0)
            return null;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d is >= -20000 and <= 20000)
        {
            value = Math.Round(d);
            return null;
        }
        return "Число от -20000 до 20000 или пусто";
    }
}
