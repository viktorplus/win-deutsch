using System.Diagnostics;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Tests.Storage;

public sealed class PhraseRepositoryTests : RepositoryTestBase
{
    [Fact]
    public void AddTopic_AndGetTopics_ReturnInInsertionOrderWithFields()
    {
        var work = Repo.AddTopic("Работа", "Базовый набор", isBuiltIn: true);
        var mine = Repo.AddTopic("Моё", null);

        var topics = Repo.GetTopics();

        Assert.Equal(new[] { "Работа", "Моё" }, topics.Select(t => t.Name));
        Assert.Equal(work, topics[0]);
        Assert.Equal("Базовый набор", topics[0].Category);
        Assert.True(topics[0].IsBuiltIn);
        Assert.True(topics[0].IsEnabled);
        Assert.False(topics[1].IsBuiltIn);
        Assert.Null(topics[1].Category);
        Assert.True(mine.SortOrder > work.SortOrder);
    }

    [Fact]
    public void RenameTopic_AndSetTopicEnabled_ArePersisted()
    {
        var t = Repo.AddTopic("Old", "Cat");

        Repo.RenameTopic(t.Id, "New", null);
        Repo.SetTopicEnabled(t.Id, false);

        var saved = Repo.GetTopics().Single();
        Assert.Equal("New", saved.Name);
        Assert.Null(saved.Category);
        Assert.False(saved.IsEnabled);
    }

    [Fact]
    public void DeleteTopic_CascadesPhrasesAndEvents()
    {
        var gone = Repo.AddTopic("Gone", null);
        var kept = Repo.AddTopic("Kept", null);
        var p = Repo.AddPhrase(gone.Id, "Hallo", "Привет");
        var k = Repo.AddPhrase(kept.Id, "Tschüss", "Пока");
        Repo.AddEvent(p.Id, DisplayMode.Widget, ShowOutcome.Shown);

        Repo.DeleteTopic(gone.Id);

        Assert.Equal(new[] { "Kept" }, Repo.GetTopics().Select(t => t.Name));
        Assert.Null(Repo.GetPhrase(p.Id));
        Assert.Equal(new[] { k.Id }, Repo.GetPhrases().Select(x => x.Id));
        Assert.Empty(Repo.GetHistory(10));
    }

    [Fact]
    public void DeleteTopics_RemovesAllGivenTopicsWithTheirPhrases()
    {
        var a = Repo.AddTopic("A", "Cat");
        var b = Repo.AddTopic("B", "Cat");
        var kept = Repo.AddTopic("Kept", null);
        Repo.AddPhrase(a.Id, "Hallo", "Привет");
        Repo.AddPhrase(b.Id, "Danke", "Спасибо");
        var k = Repo.AddPhrase(kept.Id, "Tschüss", "Пока");

        Repo.DeleteTopics([a.Id, b.Id]);

        Assert.Equal(new[] { "Kept" }, Repo.GetTopics().Select(t => t.Name));
        Assert.Equal(new[] { k.Id }, Repo.GetPhrases().Select(x => x.Id));
    }

    [Fact]
    public void GetOrCreateTopic_MatchesNameCaseInsensitively_ElseCreates()
    {
        var work = Repo.AddTopic("Работа", null);

        var same = Repo.GetOrCreateTopic("работа", "ignored");
        var created = Repo.GetOrCreateTopic("Reisen", null);

        Assert.Equal(work.Id, same.Id);
        Assert.NotEqual(work.Id, created.Id);
        Assert.Equal("Reisen", created.Name);
        Assert.Equal(2, Repo.GetTopics().Count);
    }

    [Fact]
    public void AddPhrase_SetsDefaultsAndCreatedAtFromTimeProvider()
    {
        var t = Repo.AddTopic("T", null);

        var p = Repo.AddPhrase(t.Id, "Guten Morgen", "Доброе утро");

        var saved = Repo.GetPhrase(p.Id);
        Assert.Equal(p, saved);
        Assert.Equal(new Phrase(p.Id, t.Id, "Guten Morgen", "Доброе утро", PhraseStatus.Active, 0, false, null, 0, 0, 0, null, Start), saved);
    }

    [Fact]
    public void GetPhrases_FiltersByTopic_NullReturnsAll()
    {
        var a = Repo.AddTopic("A", null);
        var b = Repo.AddTopic("B", null);
        Repo.AddPhrase(a.Id, "a1", "");
        Repo.AddPhrase(a.Id, "a2", "");
        Repo.AddPhrase(b.Id, "b1", "");

        Assert.Equal(new[] { "a1", "a2" }, Repo.GetPhrases(a.Id).Select(p => p.German));
        Assert.Equal(new[] { "b1" }, Repo.GetPhrases(b.Id).Select(p => p.German));
        Assert.Equal(3, Repo.GetPhrases().Count);
        Assert.Null(Repo.GetPhrase(9999));
    }

    [Fact]
    public void UpdateMoveDelete_ChangeOnlyTargetedPhrases()
    {
        var a = Repo.AddTopic("A", null);
        var b = Repo.AddTopic("B", null);
        var p1 = Repo.AddPhrase(a.Id, "eins", "один");
        var p2 = Repo.AddPhrase(a.Id, "zwei", "два");
        var p3 = Repo.AddPhrase(a.Id, "drei", "три");

        Repo.UpdatePhraseText(p1.Id, "Eins!", "Один!");
        Repo.MovePhrases(new[] { p1.Id, p2.Id }, b.Id);
        Repo.DeletePhrases(new[] { p3.Id });

        Assert.Equal(("Eins!", "Один!"), (Repo.GetPhrase(p1.Id)!.German, Repo.GetPhrase(p1.Id)!.Russian));
        Assert.Equal(new[] { p1.Id, p2.Id }, Repo.GetPhrases(b.Id).Select(p => p.Id));
        Assert.Empty(Repo.GetPhrases(a.Id));
        Assert.Null(Repo.GetPhrase(p3.Id));
    }

    [Fact]
    public void SetStatus_SetLevel_Snooze_ApplyToAllGivenIds()
    {
        var t = Repo.AddTopic("T", null);
        var p1 = Repo.AddPhrase(t.Id, "eins", "");
        var p2 = Repo.AddPhrase(t.Id, "zwei", "");
        var p3 = Repo.AddPhrase(t.Id, "drei", "");
        var until = Start.AddHours(3);

        Repo.SetStatus(new[] { p1.Id, p2.Id }, PhraseStatus.Excluded);
        Repo.SetLevel(new[] { p2.Id, p3.Id }, 4);
        Repo.Snooze(new[] { p1.Id, p3.Id }, until);

        var r1 = Repo.GetPhrase(p1.Id)!;
        var r2 = Repo.GetPhrase(p2.Id)!;
        var r3 = Repo.GetPhrase(p3.Id)!;
        Assert.Equal((PhraseStatus.Excluded, 0, until), (r1.Status, r1.Level, r1.SnoozedUntil));
        Assert.Equal((PhraseStatus.Excluded, 4, (DateTimeOffset?)null), (r2.Status, r2.Level, r2.SnoozedUntil));
        Assert.Equal((PhraseStatus.Active, 4, until), (r3.Status, r3.Level, r3.SnoozedUntil));
    }

    [Fact]
    public void SaveLearningState_RoundTripsAllLearningFields()
    {
        var t = Repo.AddTopic("T", null);
        var p = Repo.AddPhrase(t.Id, "Hallo", "Привет");
        var changed = p with
        {
            Level = 3, RepeatMore = true, Status = PhraseStatus.Excluded,
            SnoozedUntil = new DateTimeOffset(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(2)),
            ShowCount = 7, CorrectCount = 4, WrongCount = 2,
            LastShownAt = Start.AddMinutes(5),
            German = "ignored", Russian = "ignored",
        };

        Repo.SaveLearningState(changed);

        var saved = Repo.GetPhrase(p.Id)!;
        Assert.Equal(changed with { German = "Hallo", Russian = "Привет" }, saved);
    }

    [Fact]
    public void AddPhrasesBulk_Inserts5000RowsUnderTwoSeconds()
    {
        var t = Repo.AddTopic("T", null);
        var rows = Enumerable.Range(0, 5000).Select(i => (t.Id, $"Satz {i}", $"фраза {i}")).ToList();

        var sw = Stopwatch.StartNew();
        var inserted = Repo.AddPhrasesBulk(rows);
        sw.Stop();

        Assert.Equal(5000, inserted);
        Assert.Equal(5000, Repo.GetPhrases(t.Id).Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public void ExistsInTopic_ComparesNormalizedGermanWithinTopicOnly()
    {
        var a = Repo.AddTopic("A", null);
        var b = Repo.AddTopic("B", null);
        Repo.AddPhrase(a.Id, "Guten Morgen", "");
        Repo.AddPhrase(a.Id, "Übung", "");

        Assert.True(Repo.ExistsInTopic(a.Id, "  guten   MORGEN "));
        Assert.True(Repo.ExistsInTopic(a.Id, "ÜBUNG"));
        Assert.False(Repo.ExistsInTopic(b.Id, "Guten Morgen"));
        Assert.False(Repo.ExistsInTopic(a.Id, "Guten Abend"));
    }

    [Fact]
    public void GetCandidates_DropsExcludedFutureSnoozedDisabledTopicAndEmptyGerman()
    {
        var on = Repo.AddTopic("On", null);
        var off = Repo.AddTopic("Off", null);
        var plain = Repo.AddPhrase(on.Id, "plain", "");
        var excluded = Repo.AddPhrase(on.Id, "excluded", "");
        var snoozedFuture = Repo.AddPhrase(on.Id, "future", "");
        var snoozedPast = Repo.AddPhrase(on.Id, "past", "");
        var snoozedNow = Repo.AddPhrase(on.Id, "now", "");
        Repo.AddPhrase(on.Id, "   ", "пусто");
        Repo.AddPhrase(off.Id, "in disabled topic", "");
        Repo.SetStatus(new[] { excluded.Id }, PhraseStatus.Excluded);
        Repo.Snooze(new[] { snoozedFuture.Id }, Start.AddMinutes(1));
        Repo.Snooze(new[] { snoozedPast.Id }, Start.AddMinutes(-1));
        Repo.Snooze(new[] { snoozedNow.Id }, Start);
        Repo.SetTopicEnabled(off.Id, false);

        var ids = Repo.GetCandidates().Select(p => p.Id).OrderBy(x => x);

        Assert.Equal(new[] { plain.Id, snoozedPast.Id, snoozedNow.Id }, ids);
    }

    [Fact]
    public void GetCandidates_IncludesSnoozedPhraseOnceTimePasses()
    {
        var t = Repo.AddTopic("T", null);
        var p = Repo.AddPhrase(t.Id, "später", "");
        Repo.Snooze(new[] { p.Id }, Start.AddHours(1));
        Assert.Empty(Repo.GetCandidates());

        Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(new[] { p.Id }, Repo.GetCandidates().Select(x => x.Id));
    }
}
