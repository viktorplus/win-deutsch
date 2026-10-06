using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace WinDeutsch.Core.Backup;

public static class BackupService
{
    private const string DatabaseEntry = "data.db";
    private const string SettingsEntry = "settings.json";

    // Zip with a consistent copy of data.db (VACUUM INTO) and settings.json; missing files are left out.
    public static void CreateBackup(string zipPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "WinDeutsch-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var dbCopy = Path.Combine(temp, DatabaseEntry);
            if (File.Exists(AppPaths.DatabaseFile))
                VacuumInto(AppPaths.DatabaseFile, dbCopy);
            if (File.Exists(AppPaths.SettingsFile))
                File.Copy(AppPaths.SettingsFile, Path.Combine(temp, SettingsEntry));

            File.Delete(zipPath);
            ZipFile.CreateFromDirectory(temp, zipPath);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // Saves the current state to BackupsDir\auto-before-restore-<ts>.zip, then replaces data.db
    // (and settings.json when the copy has it). The caller reopens Database afterwards.
    public static void Restore(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var db = archive.GetEntry(DatabaseEntry)
                 ?? throw new InvalidDataException("В резервной копии нет файла data.db.");
        var settings = archive.GetEntry(SettingsEntry);

        Directory.CreateDirectory(AppPaths.BackupsDir);
        var stamp = TimeProvider.System.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        CreateBackup(Path.Combine(AppPaths.BackupsDir, $"auto-before-restore-{stamp}.zip"));

        db.ExtractToFile(AppPaths.DatabaseFile, overwrite: true);
        settings?.ExtractToFile(AppPaths.SettingsFile, overwrite: true);
    }

    private static void VacuumInto(string source, string target)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(cs);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "VACUUM INTO $target";
        cmd.Parameters.AddWithValue("$target", target);
        cmd.ExecuteNonQuery();
    }
}
