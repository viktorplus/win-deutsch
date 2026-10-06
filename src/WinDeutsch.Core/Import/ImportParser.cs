namespace WinDeutsch.Core.Import;

public sealed record ImportRow(int LineNumber, string German, string Russian, string? TopicName);

public sealed record ImportError(int LineNumber, string Message);

public sealed record ImportParseResult(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportError> Errors);

public static class ImportParser
{
    // Checked in this order on every line; the first one present splits it.
    private static readonly string[] Separators = ["\t", ";", " — ", " - "];

    // One line = "german<sep>russian[<sep>topic]". Blank lines and lines starting with '#' are skipped;
    // a line without a separator is a phrase without translation; an empty German text is an error.
    public static ImportParseResult Parse(string text)
    {
        var rows = new List<ImportRow>();
        var errors = new List<ImportError>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = Separators.FirstOrDefault(s => line.Contains(s, StringComparison.Ordinal));
            var parts = separator is null ? [line] : line.Split(separator, 3);
            var german = parts[0].Trim();
            var russian = parts.Length > 1 ? parts[1].Trim() : "";
            var topic = parts.Length > 2 ? parts[2].Trim() : "";

            if (german.Length == 0)
                errors.Add(new ImportError(i + 1, "пустой немецкий текст"));
            else
                rows.Add(new ImportRow(i + 1, german, russian, topic.Length == 0 ? null : topic));
        }
        return new ImportParseResult(rows, errors);
    }

    // The duplicate key: trim, collapse whitespace, ToLowerInvariant.
    public static string Normalize(string german) =>
        string.Join(' ', german.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
