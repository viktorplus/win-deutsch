using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Import;

public sealed record ImportPlan(int ToAdd, int Duplicates, IReadOnlyList<ImportError> Errors);

public sealed class ImportService
{
    private readonly PhraseRepository _repo;

    public ImportService(PhraseRepository repo) => _repo = repo;

    public ImportPlan Preview(ImportParseResult parsed, long defaultTopicId)
    {
        var (toAdd, duplicates) = Plan(parsed, defaultTopicId, TopicIdsByName());
        return new ImportPlan(toAdd.Count, duplicates, parsed.Errors);
    }

    // Creates topics named in the 3rd column (category null); skips duplicates in the db and within the file.
    public int Commit(ImportParseResult parsed, long defaultTopicId)
    {
        foreach (var name in parsed.Rows.Select(r => r.TopicName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            _repo.GetOrCreateTopic(name, null);

        var (toAdd, _) = Plan(parsed, defaultTopicId, TopicIdsByName());
        return toAdd.Count == 0 ? 0 : _repo.AddPhrasesBulk(toAdd);
    }

    // Same case-insensitive, trimmed name match as PhraseRepository.GetOrCreateTopic (first by sort order wins).
    private Dictionary<string, long> TopicIdsByName()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in _repo.GetTopics())
            map.TryAdd(topic.Name.Trim(), topic.Id);
        return map;
    }

    // Topics missing from `topicIds` (preview only) get a placeholder key and start empty.
    private (List<(long TopicId, string German, string Russian)> ToAdd, int Duplicates) Plan(
        ImportParseResult parsed, long defaultTopicId, Dictionary<string, long> topicIds)
    {
        var known = new Dictionary<long, HashSet<string>>();
        var toAdd = new List<(long, string, string)>();
        var duplicates = 0;
        foreach (var row in parsed.Rows)
        {
            var topicId = row.TopicName is null ? defaultTopicId : TopicKey(row.TopicName.Trim());
            if (!known.TryGetValue(topicId, out var germans))
            {
                germans = topicId > 0
                    ? _repo.GetPhrases(topicId).Select(p => ImportParser.Normalize(p.German)).ToHashSet()
                    : [];
                known[topicId] = germans;
            }

            if (germans.Add(ImportParser.Normalize(row.German)))
                toAdd.Add((topicId, row.German, row.Russian));
            else
                duplicates++;
        }
        return (toAdd, duplicates);

        long TopicKey(string name)
        {
            if (!topicIds.TryGetValue(name, out var id))
                topicIds[name] = id = -(topicIds.Count + 1);
            return id;
        }
    }
}
