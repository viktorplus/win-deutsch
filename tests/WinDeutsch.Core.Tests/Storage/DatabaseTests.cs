using Microsoft.Data.Sqlite;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Tests.Storage;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));

    public DatabaseTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    [Fact]
    public void Open_OnMissingFile_CreatesSchemaVersion1WithForeignKeys()
    {
        var db = new Database(Path.Combine(_dir, "data.db"));

        using var c = db.Open();

        Assert.Equal(1L, Scalar(c, "PRAGMA user_version"));
        Assert.Equal(1L, Scalar(c, "PRAGMA foreign_keys"));
        Assert.Equal(3L, Scalar(c, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('topics','phrases','show_events')"));
        Assert.Equal(3L, Scalar(c, "SELECT count(*) FROM sqlite_master WHERE type='index' AND name IN ('ix_phrases_topic','ix_events_at','ix_events_phrase')"));
    }

    [Fact]
    public void Open_Again_KeepsData()
    {
        var path = Path.Combine(_dir, "data.db");
        using (var c = new Database(path).Open())
            Scalar(c, "INSERT INTO topics(name) VALUES ('Работа')");

        using var c2 = new Database(path).Open();

        Assert.Equal("Работа", Scalar(c2, "SELECT name FROM topics"));
        Assert.Equal(1L, Scalar(c2, "PRAGMA user_version"));
    }

    [Fact]
    public void Open_OnNonSqliteFile_ThrowsDatabaseCorruptException()
    {
        var path = Path.Combine(_dir, "data.db");
        File.WriteAllText(path, "this is definitely not a sqlite database, just some text padding padding padding");

        Assert.Throws<DatabaseCorruptException>(() => new Database(path).Open());
    }

    [Fact]
    public void Open_DoesNotHoldFileAfterDispose()
    {
        var path = Path.Combine(_dir, "data.db");
        using (new Database(path).Open()) { }

        File.Move(path, path + ".moved"); // would fail if a pooled handle kept the file open

        Assert.True(File.Exists(path + ".moved"));
    }
}
