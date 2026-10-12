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

    public new void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        base.Dispose();
    }
}
