using WinDeutsch.Core.Settings;

namespace WinDeutsch.Core.Scheduling;

public enum PauseOption { Minutes15, Hour1, UntilTomorrow, Indefinite }

public sealed class ShowScheduler
{
    private static readonly TimeOnly MorningEnd = new(6, 0);

    private readonly TimeProvider _time;
    private readonly Random _random;

    public ShowScheduler(TimeProvider time, Random random)
    {
        _time = time;
        _random = random;
    }

    public DateTimeOffset? NextDueAt { get; private set; }

    // next = now + interval; a random interval is drawn in whole seconds within [MinSeconds, MaxSeconds].
    public void Reset(IntervalSettings interval)
    {
        var seconds = interval.Kind == IntervalKind.Random
            ? _random.Next(interval.MinSeconds, Math.Max(interval.MinSeconds, interval.MaxSeconds) + 1)
            : interval.Seconds;
        NextDueAt = _time.GetUtcNow().AddSeconds(seconds);
    }

    public bool IsDue() => NextDueAt is { } due && _time.GetUtcNow() >= due;

    // [From, To) with wrap past midnight; From == To means an empty range.
    public static bool IsInQuietHours(TimeOnly now, QuietHoursSettings q) => q.Enabled && now.IsBetween(q.From, q.To);

    // UntilTomorrow is the next 06:00 after nowLocal (after midnight that is this morning).
    public static DateTimeOffset PauseUntil(PauseOption option, DateTimeOffset nowLocal) => option switch
    {
        PauseOption.Minutes15 => nowLocal.AddMinutes(15),
        PauseOption.Hour1 => nowLocal.AddHours(1),
        PauseOption.UntilTomorrow => NextMorning(nowLocal),
        PauseOption.Indefinite => DateTimeOffset.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
    };

    private static DateTimeOffset NextMorning(DateTimeOffset nowLocal)
    {
        var morning = new DateTimeOffset(nowLocal.Date + MorningEnd.ToTimeSpan(), nowLocal.Offset);
        return morning > nowLocal ? morning : morning.AddDays(1);
    }
}
