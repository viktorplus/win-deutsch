using Microsoft.Data.Sqlite;

namespace WinDeutsch.Core.Storage;

public sealed class DatabaseCorruptException : Exception
{
    public DatabaseCorruptException(string message, Exception? inner = null) : base(message, inner) { }
}

// Owns the database file and the schema.
public sealed class Database
{
    private const int SchemaVersion = 1;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADb = 26;

    private const string SchemaSql = """
        CREATE TABLE topics (
          id INTEGER PRIMARY KEY,
          name TEXT NOT NULL,
          category TEXT NULL,
          is_enabled INTEGER NOT NULL DEFAULT 1,
          is_built_in INTEGER NOT NULL DEFAULT 0,
          sort_order INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE phrases (
          id INTEGER PRIMARY KEY,
          topic_id INTEGER NOT NULL REFERENCES topics(id) ON DELETE CASCADE,
          german TEXT NOT NULL,
          russian TEXT NOT NULL DEFAULT '',
          status INTEGER NOT NULL DEFAULT 0,
          level INTEGER NOT NULL DEFAULT 0,
          repeat_more INTEGER NOT NULL DEFAULT 0,
          snoozed_until TEXT NULL,
          show_count INTEGER NOT NULL DEFAULT 0,
          correct_count INTEGER NOT NULL DEFAULT 0,
          wrong_count INTEGER NOT NULL DEFAULT 0,
          last_shown_at TEXT NULL,
          created_at TEXT NOT NULL
        );
        CREATE INDEX ix_phrases_topic ON phrases(topic_id);
        CREATE TABLE show_events (
          id INTEGER PRIMARY KEY,
          phrase_id INTEGER NOT NULL REFERENCES phrases(id) ON DELETE CASCADE,
          at TEXT NOT NULL,
          mode INTEGER NOT NULL,
          outcome INTEGER NOT NULL
        );
        CREATE INDEX ix_events_at ON show_events(at);
        CREATE INDEX ix_events_phrase ON show_events(phrase_id);
        PRAGMA user_version = 1;
        """;

    private readonly string _connectionString;
    private bool _schemaEnsured;

    public Database(string filePath)
    {
        FilePath = filePath;
        // No pooling: the file must be free for backup/restore/rename as soon as connections are disposed.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();
    }

    public string FilePath { get; }

    // Returns an opened connection with foreign keys on; the schema is ensured on the first call.
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            if (!_schemaEnsured)
            {
                EnsureSchema(connection);
                _schemaEnsured = true;
            }
            return connection;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADb)
        {
            connection.Dispose();
            throw new DatabaseCorruptException($"Файл базы повреждён: {FilePath}", ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(check.ExecuteScalar());
        if (version >= SchemaVersion)
            return;

        using var tx = connection.BeginTransaction();
        using var create = connection.CreateCommand();
        create.Transaction = tx;
        create.CommandText = SchemaSql;
        create.ExecuteNonQuery();
        tx.Commit();
    }
}
