namespace WinDeutsch.Core;

public static class AppPaths
{
    private const string DataDirVariable = "WINDEUTSCH_DATA_DIR";

    // %LOCALAPPDATA%\WinDeutsch, used when WINDEUTSCH_DATA_DIR is not set. Not created by this getter.
    public static string DefaultDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDeutsch");

    // %WINDEUTSCH_DATA_DIR% or DefaultDataDir; the folder is created on access.
    public static string DataDir
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirVariable);
            var dir = string.IsNullOrWhiteSpace(overridden) ? DefaultDataDir : overridden;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabaseFile => Path.Combine(DataDir, "data.db");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "log.txt");
    public static string BackupsDir => Path.Combine(DataDir, "backups");
}
