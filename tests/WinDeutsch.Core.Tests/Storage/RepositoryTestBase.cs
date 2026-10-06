using WinDeutsch.Core.Storage;

namespace WinDeutsch.Core.Tests.Storage;

public abstract class RepositoryTestBase : IDisposable
{
    protected static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));

    protected RepositoryTestBase(TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        Directory.CreateDirectory(_dir);
        Time = new FakeTimeProvider(now ?? Start, zone);
        Repo = new PhraseRepository(new Database(Path.Combine(_dir, "data.db")), Time);
    }

    protected FakeTimeProvider Time { get; }

    protected PhraseRepository Repo { get; }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
