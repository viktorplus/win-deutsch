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

    // Imports the files whose path relative to the folder is not in `imported` (case-insensitive) and returns
    // those paths, so a dictionary is connected once and a deleted one does not come back. A file that fails
    // goes to onError and is left out (tried again next time).
    public static IReadOnlyList<string> ImportNew(PhraseRepository repo, string folder, IReadOnlyCollection<string> imported,
        Action<string, Exception> onError)
    {
        var done = new HashSet<string>(imported, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        foreach (var file in List(folder))
        {
            var path = Path.GetRelativePath(folder, file.Path);
            if (done.Contains(path))
                continue;
            try
            {
                Import(repo, file);
                added.Add(path);
            }
            catch (Exception ex)
            {
                onError(path, ex);
            }
        }
        return added;
    }
}
