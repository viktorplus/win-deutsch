namespace WinDeutsch.Core.Tests;

public sealed class FakeTimeProvider : TimeProvider
{
    private readonly TimeZoneInfo _zone;

    public FakeTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
    {
        UtcNow = utcNow.ToUniversalTime();
        _zone = zone ?? TimeZoneInfo.Utc;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override TimeZoneInfo LocalTimeZone => _zone;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
