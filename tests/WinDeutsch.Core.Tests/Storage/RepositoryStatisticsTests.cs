using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Tests.Storage;

public sealed class RepositoryStatisticsTests : RepositoryTestBase
{
    // Local zone UTC+3; "now" is 01:30 local on 2026-10-06 while UTC is still 2026-10-05.
    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 22, 30, 0, TimeSpan.Zero);

    public RepositoryStatisticsTests() : base(Plus3, Now) { }

    private void EventAt(DateTimeOffset utc, long phraseId, ShowOutcome outcome, DisplayMode mode = DisplayMode.Widget)
    {
        Time.UtcNow = utc;
        Repo.AddEvent(phraseId, mode, outcome);
        Time.UtcNow = Now;
    }

    [Fact]
    public void GetStatistics_CountsFamiliarityFromPhraseFields()
    {
        var t = Repo.AddTopic("T", null);
        Repo.AddPhrase(t.Id, "neu", "");
        var unknown = Repo.AddPhrase(t.Id, "unbekannt", "незнакомо");
        var learning = Repo.AddPhrase(t.Id, "lernen", "учу");
        var known = Repo.AddPhrase(t.Id, "bekannt", "");
        var excluded = Repo.AddPhrase(t.Id, "raus", "");
        Repo.SaveLearningState(unknown with { ShowCount = 3, Level = 1, WrongCount = 4 });
        Repo.SaveLearningState(learning with { ShowCount = 3, Level = 3, WrongCount = 1 });
        Repo.SaveLearningState(known with { ShowCount = 5, Level = 5 });
        Repo.SaveLearningState(excluded with { ShowCount = 2, Level = 5, Status = PhraseStatus.Excluded });

        var s = Repo.GetStatistics();

        Assert.Equal((5, 1, 1, 1, 1, 1), (s.TotalPhrases, s.New, s.Unknown, s.Learning, s.Known, s.Excluded));
        Assert.Equal(new[] { ("unbekannt", "незнакомо", 4), ("lernen", "учу", 1) }, s.Hardest);
    }

    [Fact]
    public void GetStatistics_Hardest_IsTopTenByWrongCount()
    {
        var t = Repo.AddTopic("T", null);
        for (var i = 1; i <= 12; i++)
        {
            var p = Repo.AddPhrase(t.Id, $"p{i}", "");
            Repo.SaveLearningState(p with { ShowCount = 1, WrongCount = i });
        }

        var hardest = Repo.GetStatistics().Hardest;

        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(i => $"p{i}"), hardest.Select(h => h.German));
    }

    [Fact]
    public void GetStatistics_TodayAndLast7Days_UseLocalDateFromTimeProvider()
    {
        var t = Repo.AddTopic("T", null);
        var p = Repo.AddPhrase(t.Id, "Hallo", "");
        // Local today starts 2026-10-05T21:00Z; local 7-day window starts 2026-09-29T21:00Z.
        EventAt(new DateTimeOffset(2026, 10, 5, 21, 30, 0, TimeSpan.Zero), p.Id, ShowOutcome.Shown);   // today
        EventAt(new DateTimeOffset(2026, 10, 5, 21, 31, 0, TimeSpan.Zero), p.Id, ShowOutcome.Correct); // today quiz
        EventAt(new DateTimeOffset(2026, 10, 5, 21, 32, 0, TimeSpan.Zero), p.Id, ShowOutcome.Wrong);   // today quiz
        EventAt(new DateTimeOffset(2026, 10, 5, 21, 33, 0, TimeSpan.Zero), p.Id, ShowOutcome.Known);   // not a show
        EventAt(new DateTimeOffset(2026, 10, 5, 20, 30, 0, TimeSpan.Zero), p.Id, ShowOutcome.Shown);   // yesterday local
        EventAt(new DateTimeOffset(2026, 10, 5, 20, 31, 0, TimeSpan.Zero), p.Id, ShowOutcome.Correct); // yesterday quiz
        EventAt(new DateTimeOffset(2026, 9, 29, 21, 30, 0, TimeSpan.Zero), p.Id, ShowOutcome.Shown);   // 6 days ago local
        EventAt(new DateTimeOffset(2026, 9, 29, 20, 30, 0, TimeSpan.Zero), p.Id, ShowOutcome.Shown);   // 7 days ago local

        var s = Repo.GetStatistics();

        Assert.Equal(1, s.ShowsToday);
        Assert.Equal(3, s.ShowsLast7Days);
        Assert.Equal((2, 1), (s.QuizzesToday, s.CorrectToday));
        Assert.Equal((3, 2), (s.QuizzesTotal, s.CorrectTotal));
    }

    [Fact]
    public void GetHistory_ReturnsMostRecentFirstWithGermanAndLimit()
    {
        var t = Repo.AddTopic("T", null);
        var a = Repo.AddPhrase(t.Id, "Alpha", "");
        var b = Repo.AddPhrase(t.Id, "Beta", "");
        EventAt(Now.AddMinutes(-3), a.Id, ShowOutcome.Shown, DisplayMode.Popup);
        EventAt(Now.AddMinutes(-2), b.Id, ShowOutcome.Later, DisplayMode.Widget);
        EventAt(Now.AddMinutes(-1), a.Id, ShowOutcome.Excluded, DisplayMode.Popup);

        var history = Repo.GetHistory(2);

        Assert.Equal(2, history.Count);
        Assert.Equal("Alpha", history[0].German);
        Assert.Equal((a.Id, Now.AddMinutes(-1), DisplayMode.Popup, ShowOutcome.Excluded),
            (history[0].Event.PhraseId, history[0].Event.At, history[0].Event.Mode, history[0].Event.Outcome));
        Assert.Equal(("Beta", ShowOutcome.Later, DisplayMode.Widget),
            (history[1].German, history[1].Event.Outcome, history[1].Event.Mode));
        Assert.Equal(3, Repo.GetHistory(500).Count);
    }
}
