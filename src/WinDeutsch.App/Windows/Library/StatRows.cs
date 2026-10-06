namespace WinDeutsch.App.Windows.Library;

// Plain row types for the statistics and history grids (WPF binds to properties, not tuple fields).
internal sealed record StatItem(string Label, string Value);

internal sealed record HardestRow(string German, string Russian, int Wrong);

internal sealed record HistoryRow(
    string At, DateTimeOffset AtSort, string German, string Mode, string Outcome, int ShowCount, string LastShown);
