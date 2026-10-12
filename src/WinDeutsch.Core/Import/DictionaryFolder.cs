using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Import;

public sealed record DictionaryFile(string Path, string? Category, string Topic);

// The "dictionaries" folder next to the exe: <folder>\<category>\<topic>.tsv, files at the top have no category.
public static class DictionaryFolder
{
    public static string Default => Path.Combine(AppContext.BaseDirectory, "dictionaries");

    // Files in subfolders first (by category, then topic), then the ones at the top; empty when the folder is missing.
    public static IReadOnlyList<DictionaryFile> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.tsv", SearchOption.AllDirectories)
            .Select(path =>
            {
                var dir = Path.GetRelativePath(folder, Path.GetDirectoryName(path)!);
                return new DictionaryFile(path, dir == "." ? null : dir, Path.GetFileNameWithoutExtension(path));
            })
            .OrderBy(f => f.Category is null ? 1 : 0)
            .ThenBy(f => f.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.Topic, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // Adds the file's phrases to its topic (created in the file's category when missing); duplicates are skipped.
    // Returns the number of phrases added.
    public static int Import(PhraseRepository repo, DictionaryFile file)
    {
        var parsed = ImportParser.Parse(TextFileReader.ReadAllTextDetect(file.Path));
        var topic = repo.GetOrCreateTopic(file.Topic, file.Category);
        return new ImportService(repo).Commit(parsed, topic.Id);
    }
}
