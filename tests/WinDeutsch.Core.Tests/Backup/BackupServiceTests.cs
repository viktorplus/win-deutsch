using System.IO.Compression;
using WinDeutsch.Core.Backup;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Tests.Backup;

// Shares the process-wide WINDEUTSCH_DATA_DIR variable with AppPathsAndLogTests, hence the same collection.
[Collection("WINDEUTSCH_DATA_DIR")]
public sealed class BackupServiceTests : IDisposable
{
    private const string Variable = "WINDEUTSCH_DATA_DIR";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(Variable, Path.Combine(_root, "data"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, _previous);
        Directory.Delete(_root, recursive: true);
    }

    private PhraseRepository OpenRepo() => new(new Database(AppPaths.DatabaseFile), _time);

    [Fact]
    public void CreateBackup_ZipHoldsDatabaseAndSettings()
    {
        var repo = OpenRepo();
        repo.AddTopic("Тема", null);
        File.WriteAllText(AppPaths.SettingsFile, """{"quizPercent":40}""");
        var zip = Path.Combine(_root, "out", "copy.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);

        BackupService.CreateBackup(zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "data.db", "settings.json" }, archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public void Restore_BringsBackDataAndSettings_AndSavesCurrentStateFirst()
    {
        var repo = OpenRepo();
        var topic = repo.AddTopic("Тема", null);
        repo.AddPhrase(topic.Id, "Vor dem Backup", "До копии");
        File.WriteAllText(AppPaths.SettingsFile, """{"quizPercent":40}""");
        var zip = Path.Combine(_root, "copy.zip");
        BackupService.CreateBackup(zip);

        repo.AddPhrase(topic.Id, "Nach dem Backup", "После копии");
        File.WriteAllText(AppPaths.SettingsFile, """{"quizPercent":90}""");

        BackupService.Restore(zip);

        Assert.Equal(new[] { "Vor dem Backup" }, OpenRepo().GetPhrases().Select(p => p.German));
        Assert.Equal("""{"quizPercent":40}""", File.ReadAllText(AppPaths.SettingsFile));

        var auto = Assert.Single(Directory.GetFiles(AppPaths.BackupsDir, "auto-before-restore-*.zip"));
        using var archive = ZipFile.OpenRead(auto);
        Assert.Contains(archive.Entries, e => e.FullName == "data.db");
    }

    [Fact]
    public void Restore_ZipWithoutDatabase_ThrowsAndKeepsCurrentData()
    {
        var repo = OpenRepo();
        repo.AddTopic("Моя тема", null);
        var zip = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("readme.txt");

        Assert.ThrowsAny<InvalidDataException>(() => BackupService.Restore(zip));
        Assert.Equal("Моя тема", Assert.Single(OpenRepo().GetTopics()).Name);
    }
}
