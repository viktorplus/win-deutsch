using WinDeutsch.Core.Import;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Tests.Storage;

namespace WinDeutsch.Core.Tests.Import;

public sealed class ImportServiceTests : RepositoryTestBase
{
    [Fact]
    public void PreviewAndCommit_CountDuplicatesInDbAndInFile_AndCreateTopics()
    {
        var target = Repo.AddTopic("Импорт", null);
        Repo.AddPhrase(target.Id, "Guten Morgen", "Доброе утро");
        var text = string.Join("\n",
            "guten   MORGEN\tДоброе утро",       // duplicate of the db (normalized)
            "Danke\tСпасибо",
            " danke \tСпасибо!",                 // duplicate inside the file
            "\tпусто",                           // error
            "Bitte\tПожалуйста\tНовая тема",
            "Bitte\tПожалуйста\tновая ТЕМА",     // same new topic, duplicate inside the file
            "Guten Morgen\tДоброе утро\tНовая тема"); // other topic -> not a duplicate
        var parsed = ImportParser.Parse(text);
        var service = new ImportService(Repo);

        var plan = service.Preview(parsed, target.Id);
        Assert.Equal(3, plan.ToAdd);
        Assert.Equal(3, plan.Duplicates);
        Assert.Equal(4, Assert.Single(plan.Errors).LineNumber);
        Assert.Single(Repo.GetTopics());   // preview creates nothing

        Assert.Equal(plan.ToAdd, service.Commit(parsed, target.Id));

        var topics = Repo.GetTopics();
        Assert.Equal(2, topics.Count);
        var created = topics.Single(t => t.Name == "Новая тема");
        Assert.Null(created.Category);
        Assert.Equal(new[] { "Guten Morgen", "Danke" }, Repo.GetPhrases(target.Id).Select(p => p.German));
        Assert.Equal(new[] { "Bitte", "Guten Morgen" }, Repo.GetPhrases(created.Id).Select(p => p.German));
    }

    [Fact]
    public void Commit_TopicColumnMatchesExistingTopicIgnoringCase()
    {
        var target = Repo.AddTopic("По умолчанию", null);
        var existing = Repo.AddTopic("Врач", "Базовый набор");
        Repo.AddPhrase(existing.Id, "Ich habe Kopfschmerzen", "У меня болит голова");

        var parsed = ImportParser.Parse("Ich habe Kopfschmerzen\tУ меня болит голова\tврач\nMir ist schlecht\tМне плохо\tврач");
        var added = new ImportService(Repo).Commit(parsed, target.Id);

        Assert.Equal(1, added);
        Assert.Equal(2, Repo.GetTopics().Count);
        Assert.Equal(2, Repo.GetPhrases(existing.Id).Count);
    }

    [Fact]
    public void Commit_RepeatedImport_AddsNothing()
    {
        var target = Repo.AddTopic("Импорт", null);
        var parsed = ImportParser.Parse("Ja\tДа\nNein\tНет\nVielleicht");
        var service = new ImportService(Repo);
        Assert.Equal(3, service.Commit(parsed, target.Id));
        Assert.Equal(0, service.Commit(parsed, target.Id));
        var plan = service.Preview(parsed, target.Id);
        Assert.Equal((0, 3), (plan.ToAdd, plan.Duplicates));
    }

    [Fact]
    public void Export_ThenImportIntoSameDb_AddsZero()
    {
        var work = Repo.AddTopic("Работа", "Базовый набор");
        var mine = Repo.AddTopic("Моя тема", null);
        Repo.AddPhrase(work.Id, "Ich bin im Homeoffice", "Я на удалёнке");
        Repo.AddPhrase(work.Id, "Tab\tim Text", "Перенос\nстроки");
        Repo.AddPhrase(mine.Id, "Ohne Übersetzung", "");
        Repo.AddPhrase(mine.Id, "Na, wie geht's? - Gut", "Ну как? — Хорошо");
        var before = Repo.GetPhrases().Count;

        var tsv = Exporter.ToTsv(Repo.GetPhrases(), Repo.GetTopics().ToDictionary(t => t.Id));
        var parsed = ImportParser.Parse(tsv);
        Assert.Empty(parsed.Errors);
        Assert.Equal(before, parsed.Rows.Count);

        var added = new ImportService(Repo).Commit(parsed, mine.Id);

        Assert.Equal(0, added);
        Assert.Equal(before, Repo.GetPhrases().Count);
        Assert.Equal(2, Repo.GetTopics().Count);
    }

    [Fact]
    public void Exporter_WritesGermanRussianTopic_AndFlattensTabsAndNewlines()
    {
        var topic = new Topic(3, "Быт", null, true, false, 1);
        var phrase = new Phrase(1, 3, "Tab\tin", "zwei\r\nZeilen", PhraseStatus.Active, 0, false, null, 0, 0, 0, null,
            DateTimeOffset.UnixEpoch);
        var tsv = Exporter.ToTsv([phrase], new Dictionary<long, Topic> { [3] = topic });
        Assert.Equal("Tab in\tzwei Zeilen\tБыт", tsv.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Commit_5000Rows_IsFast()
    {
        var target = Repo.AddTopic("Много", null);
        var text = string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"Satz Nummer {i}\tФраза {i}"));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var added = new ImportService(Repo).Commit(ImportParser.Parse(text), target.Id);
        Assert.Equal(5000, added);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
    }
}
