using System.Text;
using WinDeutsch.Core.Import;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Seed;

public static class SeedData
{
    public const string Category = "Базовый набор";
    public const string DevCategory = "Немецкий для программиста";

    // File -> topic and its category, in display order.
    private static readonly (string File, string Topic, string Category)[] Files =
    [
        ("work.tsv", "Работа", Category),
        ("household.tsv", "Быт", Category),
        ("doctor.tsv", "Врач", Category),
        ("phone.tsv", "Телефонные разговоры", Category),
        ("authorities.tsv", "Behörden", Category),
        ("smalltalk.tsv", "Small Talk", Category),
        ("travel.tsv", "Путешествия", Category),
        ("expressions.tsv", "Частые немецкие выражения", Category),
        ("dev-terms.tsv", "IT-термины", DevCategory),
        ("dev-meetings.tsv", "Daily и митинги", DevCategory),
        ("dev-codereview.tsv", "Code Review", DevCategory),
        ("dev-bugs.tsv", "Баги и инциденты", DevCategory),
        ("dev-deploy.tsv", "Деплой и инфраструктура", DevCategory),
        ("dev-career.tsv", "Собеседование и заказчик", DevCategory),
    ];

    // Reads the embedded Seed/*.tsv files.
    public static IReadOnlyList<(string TopicName, IReadOnlyList<(string German, string Russian)> Phrases)> Load() =>
        Load(ReadEmbedded);

    // readFile returns a file's text by name, or null when it is missing (the topic is then skipped).
    public static IReadOnlyList<(string TopicName, IReadOnlyList<(string German, string Russian)> Phrases)> Load(
        Func<string, string?> readFile)
    {
        var result = new List<(string, IReadOnlyList<(string, string)>)>();
        foreach (var (file, topic, _) in Files)
        {
            var text = readFile(file);
            if (text is not null)
                result.Add((topic, ParseTsv(text)));
        }
        return result;
    }

    // Only on a database without any topics (first run or a fresh database).
    public static void EnsureSeeded(PhraseRepository repo) => EnsureSeeded(repo, ReadEmbedded);

    public static void EnsureSeeded(PhraseRepository repo, Func<string, string?> readFile)
    {
        if (repo.GetTopics().Count > 0)
            return;
        var rows = new List<(long, string, string)>();
        foreach (var (topicName, phrases) in Load(readFile))
        {
            var topic = repo.AddTopic(topicName, CategoryOf(topicName), isBuiltIn: true);
            rows.AddRange(phrases.Select(p => (topic.Id, p.German, p.Russian)));
        }
        repo.AddPhrasesBulk(rows);
    }

    // Adds built-in phrases whose normalized German is in no topic at all (so renamed topics and moved phrases
    // give no duplicates), re-creating their built-in topic when needed; user phrases are left alone.
    // Returns the number of phrases added.
    public static int RestoreMissing(PhraseRepository repo) => RestoreMissing(repo, ReadEmbedded);

    public static int RestoreMissing(PhraseRepository repo, Func<string, string?> readFile)
    {
        var topics = repo.GetTopics();
        var present = repo.GetPhrases().Select(p => ImportParser.Normalize(p.German)).ToHashSet();
        var rows = new List<(long, string, string)>();
        foreach (var (topicName, phrases) in Load(readFile))
        {
            var existing = topics.FirstOrDefault(t => t.IsBuiltIn && string.Equals(t.Name, topicName, StringComparison.OrdinalIgnoreCase));
            var missing = phrases.Where(p => present.Add(ImportParser.Normalize(p.German))).ToList();
            if (missing.Count == 0)
                continue;
            var topicId = (existing ?? repo.AddTopic(topicName, CategoryOf(topicName), isBuiltIn: true)).Id;
            rows.AddRange(missing.Select(p => (topicId, p.German, p.Russian)));
        }
        return rows.Count == 0 ? 0 : repo.AddPhrasesBulk(rows);
    }

    private static string CategoryOf(string topicName) => Files.First(f => f.Topic == topicName).Category;

    // "German<TAB>Russian" per line; '#' comments and blank lines skipped; a trailing '\r' (CRLF checkout) is cut.
    private static List<(string German, string Russian)> ParseTsv(string text)
    {
        var phrases = new List<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#'))
                continue;
            var tab = line.IndexOf('\t');
            phrases.Add(tab < 0 ? (line.Trim(), "") : (line[..tab].Trim(), line[(tab + 1)..].Trim()));
        }
        return phrases;
    }

    private static string? ReadEmbedded(string file)
    {
        using var stream = typeof(SeedData).Assembly.GetManifestResourceStream($"WinDeutsch.Core.Seed.{file}");
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
