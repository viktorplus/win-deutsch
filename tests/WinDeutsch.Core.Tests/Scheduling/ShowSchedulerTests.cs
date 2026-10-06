using WinDeutsch.Core.Scheduling;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.Core.Tests.Scheduling;

public sealed class ShowSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BeforeReset_NothingIsDue()
    {
        var scheduler = new ShowScheduler(new FakeTimeProvider(Start), new Random(1));
        Assert.Null(scheduler.NextDueAt);
        Assert.False(scheduler.IsDue());
    }

    [Fact]
    public void FixedInterval_IsDueExactlyAfterSeconds()
    {
        var time = new FakeTimeProvider(Start);
        var scheduler = new ShowScheduler(time, new Random(1));
        scheduler.Reset(new IntervalSettings { Kind = IntervalKind.Fixed, Seconds = 30 });

        Assert.Equal(Start.AddSeconds(30), scheduler.NextDueAt);
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(scheduler.IsDue());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(scheduler.IsDue());
    }

    [Fact]
    public void Reset_RestartsCountdownFromNow()
    {
        var time = new FakeTimeProvider(Start);
        var scheduler = new ShowScheduler(time, new Random(1));
        var interval = new IntervalSettings { Kind = IntervalKind.Fixed, Seconds = 60 };
        scheduler.Reset(interval);
        time.Advance(TimeSpan.FromSeconds(50));
        scheduler.Reset(interval);   // manual "next"
        Assert.Equal(Start.AddSeconds(110), scheduler.NextDueAt);
    }

    [Fact]
    public void RandomInterval_StaysInRange_AndHitsBothHalves()
    {
        var time = new FakeTimeProvider(Start);
        var scheduler = new ShowScheduler(time, new Random(123));
        var interval = new IntervalSettings { Kind = IntervalKind.Random, MinSeconds = 60, MaxSeconds = 300 };
        int low = 0, high = 0;
        for (var i = 0; i < 1_000; i++)
        {
            scheduler.Reset(interval);
            var seconds = (scheduler.NextDueAt!.Value - Start).TotalSeconds;
            Assert.InRange(seconds, 60, 300);
            if (seconds < 180) low++; else high++;
        }
        Assert.True(low > 0 && high > 0, $"low={low} high={high}");
    }

    [Fact]
    public void RandomInterval_MinEqualsMax_IsExact()
    {
        var scheduler = new ShowScheduler(new FakeTimeProvider(Start), new Random(1));
        scheduler.Reset(new IntervalSettings { Kind = IntervalKind.Random, MinSeconds = 90, MaxSeconds = 90 });
        Assert.Equal(Start.AddSeconds(90), scheduler.NextDueAt);
    }

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("22:00", true)]
    [InlineData("03:30", true)]
    [InlineData("07:59", true)]
    [InlineData("08:00", false)]
    [InlineData("12:00", false)]
    [InlineData("21:59", false)]
    public void QuietHours_WrapPastMidnight(string now, bool expected)
    {
        var q = new QuietHoursSettings { Enabled = true, From = new TimeOnly(22, 0), To = new TimeOnly(8, 0) };
        Assert.Equal(expected, ShowScheduler.IsInQuietHours(TimeOnly.Parse(now), q));
    }

    [Theory]
    [InlineData("13:00", true)]
    [InlineData("12:00", true)]
    [InlineData("14:00", false)]
    [InlineData("11:59", false)]
    public void QuietHours_SameDayRange(string now, bool expected)
    {
        var q = new QuietHoursSettings { Enabled = true, From = new TimeOnly(12, 0), To = new TimeOnly(14, 0) };
        Assert.Equal(expected, ShowScheduler.IsInQuietHours(TimeOnly.Parse(now), q));
    }

    [Fact]
    public void QuietHours_Disabled_IsNeverQuiet()
    {
        var q = new QuietHoursSettings { Enabled = false, From = new TimeOnly(22, 0), To = new TimeOnly(8, 0) };
        Assert.False(ShowScheduler.IsInQuietHours(new TimeOnly(23, 0), q));
    }

    private static readonly DateTimeOffset LocalEvening = new(2026, 10, 5, 21, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void PauseUntil_Minutes15_And_Hour1()
    {
        Assert.Equal(LocalEvening.AddMinutes(15), ShowScheduler.PauseUntil(PauseOption.Minutes15, LocalEvening));
        Assert.Equal(LocalEvening.AddHours(1), ShowScheduler.PauseUntil(PauseOption.Hour1, LocalEvening));
    }

    [Fact]
    public void PauseUntil_Tomorrow_IsNext0600Local()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.FromHours(2)),
            ShowScheduler.PauseUntil(PauseOption.UntilTomorrow, LocalEvening));

        // After midnight "until tomorrow" means this morning's 06:00, not a day later.
        var night = new DateTimeOffset(2026, 10, 6, 1, 15, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.FromHours(2)),
            ShowScheduler.PauseUntil(PauseOption.UntilTomorrow, night));
    }

    [Fact]
    public void PauseUntil_Indefinite_IsMaxValue() =>
        Assert.Equal(DateTimeOffset.MaxValue, ShowScheduler.PauseUntil(PauseOption.Indefinite, LocalEvening));
}
