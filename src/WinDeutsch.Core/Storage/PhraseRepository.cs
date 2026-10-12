using System.Globalization;
using Microsoft.Data.Sqlite;
using WinDeutsch.Core.Import;
using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Storage;

public sealed record Statistics(
    int TotalPhrases, int New, int Unknown, int Learning, int Known, int Excluded,
    int ShowsToday, int ShowsLast7Days,
    int QuizzesToday, int CorrectToday, int QuizzesTotal, int CorrectTotal,
    IReadOnlyList<(string German, string Russian, int Wrong)> Hardest);

public sealed class PhraseRepository
{
    private const string PhraseColumns =
        "p.id, p.topic_id, p.german, p.russian, p.status, p.level, p.repeat_more, p.snoozed_until, " +
        "p.show_count, p.correct_count, p.wrong_count, p.last_shown_at, p.created_at";

    private readonly Database _db;
    private readonly TimeProvider _time;

    public PhraseRepository(Database db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    // ---- topics ----

    public IReadOnlyList<Topic> GetTopics()
    {
        using var c = _db.Open();
        return Query(c, "SELECT id, name, category, is_enabled, is_built_in, sort_order FROM topics ORDER BY sort_order, id",
            ReadTopic);
    }

    public Topic AddTopic(string name, string? category, bool isBuiltIn = false)
    {
        using var c = _db.Open();
        var id = Scalar<long>(c, """
            INSERT INTO topics(name, category, is_built_in, sort_order)
            VALUES ($name, $category, $builtIn, (SELECT coalesce(max(sort_order), 0) + 1 FROM topics));
            SELECT last_insert_rowid();
            """, ("$name", name), ("$category", category), ("$builtIn", isBuiltIn));
        return Query(c, "SELECT id, name, category, is_enabled, is_built_in, sort_order FROM topics WHERE id = $id",
            ReadTopic, ("$id", id)).Single();
    }

    public void RenameTopic(long topicId, string name, string? category) =>
        Execute("UPDATE topics SET name = $name, category = $category WHERE id = $id",
            ("$name", name), ("$category", category), ("$id", topicId));

    public void SetTopicEnabled(long topicId, bool enabled) =>
        Execute("UPDATE topics SET is_enabled = $on WHERE id = $id", ("$on", enabled), ("$id", topicId));

    // Phrases and their events go with the topic (ON DELETE CASCADE).
    public void DeleteTopic(long topicId) => Execute("DELETE FROM topics WHERE id = $id", ("$id", topicId));

    // Same as DeleteTopic for several topics in one transaction (deleting a category).
    public void DeleteTopics(IReadOnlyCollection<long> topicIds) =>
        ExecuteForEach(topicIds, "DELETE FROM topics WHERE id = $id", null);

    public Topic GetOrCreateTopic(string name, string? category)
    {
        var trimmed = name.Trim();
        var existing = GetTopics().FirstOrDefault(t => string.Equals(t.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        return existing ?? AddTopic(trimmed, category);
    }

    // ---- phrases ----

    public IReadOnlyList<Phrase> GetPhrases(long? topicId = null)
    {
        using var c = _db.Open();
        return topicId is null
            ? Query(c, $"SELECT {PhraseColumns} FROM phrases p ORDER BY p.id", ReadPhrase)
            : Query(c, $"SELECT {PhraseColumns} FROM phrases p WHERE p.topic_id = $t ORDER BY p.id", ReadPhrase, ("$t", topicId.Value));
    }

    public Phrase? GetPhrase(long id)
    {
        using var c = _db.Open();
        return Query(c, $"SELECT {PhraseColumns} FROM phrases p WHERE p.id = $id", ReadPhrase, ("$id", id)).SingleOrDefault();
    }

    public Phrase AddPhrase(long topicId, string german, string russian)
    {
        long id;
        using (var c = _db.Open())
        {
            id = Scalar<long>(c, """
                INSERT INTO phrases(topic_id, german, russian, created_at) VALUES ($t, $g, $r, $now);
                SELECT last_insert_rowid();
                """, ("$t", topicId), ("$g", german), ("$r", russian), ("$now", ToDb(_time.GetUtcNow())));
        }
        return GetPhrase(id)!;
    }

    public void UpdatePhraseText(long id, string german, string russian) =>
        Execute("UPDATE phrases SET german = $g, russian = $r WHERE id = $id", ("$g", german), ("$r", russian), ("$id", id));

    public void MovePhrases(IReadOnlyCollection<long> ids, long topicId) =>
        ExecuteForEach(ids, "UPDATE phrases SET topic_id = $v WHERE id = $id", topicId);

    public void DeletePhrases(IReadOnlyCollection<long> ids) =>
        ExecuteForEach(ids, "DELETE FROM phrases WHERE id = $id", null);

    public void SetStatus(IReadOnlyCollection<long> ids, PhraseStatus status) =>
        ExecuteForEach(ids, "UPDATE phrases SET status = $v WHERE id = $id", (int)status);

    public void SetLevel(IReadOnlyCollection<long> ids, int level) =>
        ExecuteForEach(ids, "UPDATE phrases SET level = $v WHERE id = $id", Math.Clamp(level, 0, 5));

    public void Snooze(IReadOnlyCollection<long> ids, DateTimeOffset until) =>
        ExecuteForEach(ids, "UPDATE phrases SET snoozed_until = $v WHERE id = $id", ToDb(until));

    // One transaction for the whole batch; returns the number of inserted rows.
    public int AddPhrasesBulk(IReadOnlyList<(long TopicId, string German, string Russian)> rows)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO phrases(topic_id, german, russian, created_at) VALUES ($t, $g, $r, $now)";
        var t = cmd.Parameters.Add("$t", SqliteType.Integer);
        var g = cmd.Parameters.Add("$g", SqliteType.Text);
        var r = cmd.Parameters.Add("$r", SqliteType.Text);
        cmd.Parameters.AddWithValue("$now", ToDb(_time.GetUtcNow()));
        var inserted = 0;
        foreach (var row in rows)
        {
            t.Value = row.TopicId;
            g.Value = row.German;
            r.Value = row.Russian;
            inserted += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return inserted;
    }

    public bool ExistsInTopic(long topicId, string german)
    {
        var target = ImportParser.Normalize(german);
        using var c = _db.Open();
        return Query(c, "SELECT german FROM phrases WHERE topic_id = $t", r => r.GetString(0), ("$t", topicId))
            .Any(g => ImportParser.Normalize(g) == target);
    }

    // ---- learning ----

    // Active, topic enabled, not snoozed (SnoozedUntil <= now or null), German non-empty.
    public IReadOnlyList<Phrase> GetCandidates()
    {
        using var c = _db.Open();
        return Query(c, $"""
            SELECT {PhraseColumns} FROM phrases p JOIN topics t ON t.id = p.topic_id
            WHERE p.status = 0 AND t.is_enabled = 1
              AND (p.snoozed_until IS NULL OR p.snoozed_until <= $now)
              AND trim(p.german) <> ''
            ORDER BY p.id
            """, ReadPhrase, ("$now", ToDb(_time.GetUtcNow())));
    }

    public void SaveLearningState(Phrase p) => Execute("""
        UPDATE phrases SET level = $level, repeat_more = $repeat, snoozed_until = $snoozed, status = $status,
          show_count = $shows, correct_count = $correct, wrong_count = $wrong, last_shown_at = $last
        WHERE id = $id
        """,
        ("$level", p.Level), ("$repeat", p.RepeatMore), ("$snoozed", ToDb(p.SnoozedUntil)), ("$status", (int)p.Status),
        ("$shows", p.ShowCount), ("$correct", p.CorrectCount), ("$wrong", p.WrongCount),
        ("$last", ToDb(p.LastShownAt)), ("$id", p.Id));

    public void AddEvent(long phraseId, DisplayMode mode, ShowOutcome outcome) =>
        Execute("INSERT INTO show_events(phrase_id, at, mode, outcome) VALUES ($p, $at, $mode, $outcome)",
            ("$p", phraseId), ("$at", ToDb(_time.GetUtcNow())), ("$mode", (int)mode), ("$outcome", (int)outcome));

    // ---- stats & history ----

    public IReadOnlyList<(ShowEvent Event, string German)> GetHistory(int limit)
    {
        using var c = _db.Open();
        return Query(c, """
            SELECT e.id, e.phrase_id, e.at, e.mode, e.outcome, p.german
            FROM show_events e JOIN phrases p ON p.id = e.phrase_id
            ORDER BY e.at DESC, e.id DESC LIMIT $limit
            """,
            r => (new ShowEvent(r.GetInt64(0), r.GetInt64(1), FromDb(r.GetString(2)),
                    (DisplayMode)r.GetInt32(3), (ShowOutcome)r.GetInt32(4)), r.GetString(5)),
            ("$limit", limit));
    }

    // Shows = Shown events; quizzes = Correct + Wrong events. "Today" is the local date of TimeProvider.LocalTimeZone;
    // "last 7 days" is local today plus the 6 days before it.
    public Statistics GetStatistics()
    {
        var phrases = GetPhrases();
        var active = phrases.Where(p => p.Status == PhraseStatus.Active).ToList();
        var byFamiliarity = active.GroupBy(LearningRules.GetFamiliarity).ToDictionary(g => g.Key, g => g.Count());
        int Count(Familiarity f) => byFamiliarity.GetValueOrDefault(f);

        var zone = _time.LocalTimeZone;
        var todayLocal = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).Date;
        var todayStart = ToDb(LocalMidnightUtc(todayLocal, zone));
        var weekStart = ToDb(LocalMidnightUtc(todayLocal.AddDays(-6), zone));

        using var c = _db.Open();
        int CountEvents(string where, params (string, object?)[] args) =>
            (int)Scalar<long>(c, $"SELECT count(*) FROM show_events WHERE {where}", args);

        const string shown = "outcome = 0";      // ShowOutcome.Shown
        const string quiz = "outcome IN (1, 2)";  // Correct, Wrong
        const string correct = "outcome = 1";

        var hardest = phrases.Where(p => p.WrongCount > 0)
            .OrderByDescending(p => p.WrongCount).ThenBy(p => p.Id)
            .Take(10)
            .Select(p => (p.German, p.Russian, p.WrongCount))
            .ToList();

        return new Statistics(
            TotalPhrases: phrases.Count,
            New: Count(Familiarity.New), Unknown: Count(Familiarity.Unknown),
            Learning: Count(Familiarity.Learning), Known: Count(Familiarity.Known),
            Excluded: phrases.Count - active.Count,
            ShowsToday: CountEvents($"{shown} AND at >= $from", ("$from", todayStart)),
            ShowsLast7Days: CountEvents($"{shown} AND at >= $from", ("$from", weekStart)),
            QuizzesToday: CountEvents($"{quiz} AND at >= $from", ("$from", todayStart)),
            CorrectToday: CountEvents($"{correct} AND at >= $from", ("$from", todayStart)),
            QuizzesTotal: CountEvents(quiz),
            CorrectTotal: CountEvents(correct),
            Hardest: hardest);
    }

    // ---- helpers ----

    private static DateTimeOffset LocalMidnightUtc(DateTime localDate, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

    private static string ToDb(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string? ToDb(DateTimeOffset? value) => value is null ? null : ToDb(value.Value);

    private static DateTimeOffset FromDb(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? FromDbNullable(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : FromDb(r.GetString(i));

    private static Topic ReadTopic(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
        r.GetBoolean(3), r.GetBoolean(4), r.GetInt32(5));

    private static Phrase ReadPhrase(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3),
        (PhraseStatus)r.GetInt32(4), r.GetInt32(5), r.GetBoolean(6), FromDbNullable(r, 7),
        r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), FromDbNullable(r, 11), FromDb(r.GetString(12)));

    private static SqliteCommand Command(SqliteConnection c, string sql, (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> read, params (string, object?)[] args)
    {
        using var cmd = Command(c, sql, args);
        using var reader = cmd.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
            result.Add(read(reader));
        return result;
    }

    private static T Scalar<T>(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(c, sql, args);
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private void Execute(string sql, params (string, object?)[] args)
    {
        using var c = _db.Open();
        using var cmd = Command(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    // Runs `sql` once per id in one transaction; `$v` is bound to `value` when the statement uses it.
    private void ExecuteForEach(IReadOnlyCollection<long> ids, string sql, object? value)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var id = cmd.Parameters.Add("$id", SqliteType.Integer);
        if (sql.Contains("$v"))
            cmd.Parameters.AddWithValue("$v", value ?? DBNull.Value);
        foreach (var each in ids)
        {
            id.Value = each;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
