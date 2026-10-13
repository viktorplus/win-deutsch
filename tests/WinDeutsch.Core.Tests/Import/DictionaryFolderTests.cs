using WinDeutsch.Core.Import;
using WinDeutsch.Core.Tests.Storage;

namespace WinDeutsch.Core.Tests.Import;

// Re-implements IDisposable so the folder is removed along with the base's database.
public sealed class DictionaryFolderTests : RepositoryTestBase, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));

    public DictionaryFolderTests() => Directory.CreateDirectory(_folder);

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void List_TakesCategoryFromSubfolderAndTopicFromFileName()
    {
        Write(Path.Combine("Медицина", "Тело.tsv"), "das Herz\tсердце");
        Write(Path.Combine("Бизнес", "Финансы.tsv"), "der Gewinn\tприбыль");
        Write("Разное.tsv", "ja\tда");
        Write(Path.Combine("Бизнес", "readme.txt"), "not a dictionary");

        var files = DictionaryFolder.List(_folder);

        Assert.Equal(
            new[] { ("Бизнес", "Финансы"), ("Медицина", "Тело"), ((string?)null, "Разное") },
            files.Select(f => (f.Category, f.Topic)));
    }

    [Fact]
    public void List_MissingFolder_IsEmpty() =>
        Assert.Empty(DictionaryFolder.List(Path.Combine(_folder, "nope")));

    [Fact]
    public void Import_CreatesTopicInCategory_AndSkipsDuplicatesOnSecondRun()
    {
        Write(Path.Combine("Медицина", "Тело.tsv"), "# Органы\ndas Herz\tсердце\ndie Niere\tпочка\n");
        var file = Assert.Single(DictionaryFolder.List(_folder));

        Assert.Equal(2, DictionaryFolder.Import(Repo, file));
        Assert.Equal(0, DictionaryFolder.Import(Repo, file));

        var topic = Assert.Single(Repo.GetTopics());
        Assert.Equal(("Тело", "Медицина", false), (topic.Name, topic.Category, topic.IsBuiltIn));
        Assert.Equal(new[] { "das Herz", "die Niere" }, Repo.GetPhrases(topic.Id).Select(p => p.German));
    }

    private static void Fail(string path, Exception ex) => throw new Xunit.Sdk.XunitException($"{path}: {ex}");

    [Fact]
    public void ImportNew_ImportsOnlyFilesNotImportedBefore_AndReturnsTheirRelativePaths()
    {
        Write(Path.Combine("Медицина", "Тело.tsv"), "das Herz\tсердце");
        Write(Path.Combine("Бизнес", "Финансы.tsv"), "der Gewinn\tприбыль");

        var first = DictionaryFolder.ImportNew(Repo, _folder, [Path.Combine("бизнес", "финансы.tsv")], Fail);

        Assert.Equal(new[] { Path.Combine("Медицина", "Тело.tsv") }, first);
        Assert.Equal(new[] { "Тело" }, Repo.GetTopics().Select(t => t.Name));

        // A deleted topic does not come back once its file is recorded as imported.
        Repo.DeleteTopic(Repo.GetTopics().Single().Id);
        Assert.Empty(DictionaryFolder.ImportNew(Repo, _folder, [.. first, Path.Combine("Бизнес", "Финансы.tsv")], Fail));
        Assert.Empty(Repo.GetTopics());
    }

    [Fact]
    public void ImportNew_ReportsUnreadableFileAndLeavesItOut_SoItIsTriedAgainNextTime()
    {
        Write("Хорошо.tsv", "ja\tда");
        Write("Занят.tsv", "nein\tнет");
        var failed = new List<string>();

        using (File.Open(Path.Combine(_folder, "Занят.tsv"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(new[] { "Хорошо.tsv" }, DictionaryFolder.ImportNew(Repo, _folder, [], (path, _) => failed.Add(path)));

        Assert.Equal(new[] { "Занят.tsv" }, failed);
    }

    public new void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        base.Dispose();
    }
}
