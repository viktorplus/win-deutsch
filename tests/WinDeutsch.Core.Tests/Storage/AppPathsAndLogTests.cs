namespace WinDeutsch.Core.Tests.Storage;

// Touches the process-wide WINDEUTSCH_DATA_DIR variable; every class that does is in this collection.
[Collection("WINDEUTSCH_DATA_DIR")]
public sealed class AppPathsAndLogTests : IDisposable
{
    private const string Variable = "WINDEUTSCH_DATA_DIR";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, _previous);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DataDir_UsesVariableAndCreatesFolder()
    {
        var dir = Path.Combine(_root, "data");
        Environment.SetEnvironmentVariable(Variable, dir);

        Assert.Equal(dir, AppPaths.DataDir);
        Assert.True(Directory.Exists(dir));
        Assert.Equal(Path.Combine(dir, "data.db"), AppPaths.DatabaseFile);
        Assert.Equal(Path.Combine(dir, "settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(dir, "log.txt"), AppPaths.LogFile);
        Assert.Equal(Path.Combine(dir, "backups"), AppPaths.BackupsDir);
    }

    [Fact]
    public void DataDir_WithoutVariable_IsLocalAppDataWinDeutsch()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDeutsch");

        // Only the path is compared; the real folder is never written to by tests.
        Assert.Equal(expected, AppPaths.DefaultDataDir);
    }

    [Fact]
    public void LogError_AppendsMessageAndExceptionToLogFile()
    {
        Environment.SetEnvironmentVariable(Variable, Path.Combine(_root, "data"));

        Log.Error("first failure");
        Log.Error("second failure", new InvalidOperationException("boom"));

        var text = File.ReadAllText(AppPaths.LogFile);
        Assert.Contains("first failure", text);
        Assert.Contains("second failure", text);
        Assert.Contains("InvalidOperationException: boom", text);
        Assert.True(text.IndexOf("first failure", StringComparison.Ordinal) < text.IndexOf("second failure", StringComparison.Ordinal));
    }

    [Fact]
    public void LogError_NeverThrows_WhenDataDirCannotBeCreated()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "file");
        File.WriteAllText(blocker, "not a folder");
        Environment.SetEnvironmentVariable(Variable, Path.Combine(blocker, "data"));

        var ex = Record.Exception(() => Log.Error("lost", new Exception("x")));

        Assert.Null(ex);
    }
}
