using System.Text;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Import;

public static class Exporter
{
    // "german\trussian\ttopic" per line, the same format ImportParser reads.
    public static string ToTsv(IReadOnlyList<Phrase> phrases, IReadOnlyDictionary<long, Topic> topics)
    {
        var sb = new StringBuilder();
        foreach (var p in phrases)
        {
            sb.Append(Clean(p.German)).Append('\t').Append(Clean(p.Russian));
            if (topics.TryGetValue(p.TopicId, out var topic))
                sb.Append('\t').Append(Clean(topic.Name));
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Clean(string text) =>
        text.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
