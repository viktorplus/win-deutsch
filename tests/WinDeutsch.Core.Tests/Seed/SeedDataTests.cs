using WinDeutsch.Core.Import;
using WinDeutsch.Core.Seed;
using WinDeutsch.Core.Tests.Storage;

namespace WinDeutsch.Core.Tests.Seed;

public sealed class SeedDataTests : RepositoryTestBase
{
    private static readonly string[] TopicOrder =
    [
        "Работа", "Быт", "Врач", "Телефонные разговоры", "Behörden", "Small Talk", "Путешествия", "Частые немецкие выражения",
        "IT-термины", "Daily и митинги", "Code Review", "Баги и инциденты", "Деплой и инфраструктура", "Собеседование и заказчик",
    ];

    private const string BaseCategory = "Базовый набор";
    private const string DevCategory = "Немецкий для программиста";

    private static string ExpectedCategory(string topic) => Array.IndexOf(TopicOrder, topic) < 8 ? BaseCategory : DevCategory;

    [Fact]
    public void Load_ReadsEmbeddedFilesInTopicOrder()
    {
        var seed = SeedData.Load();

        Assert.Equal(TopicOrder, seed.Select(s => s.TopicName));
        foreach (var (topic, phrases) in seed)
        {
            Assert.NotEmpty(phrases);
            Assert.All(phrases, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.German), topic);
                Assert.False(p.German.StartsWith('#'), topic);
                Assert.DoesNotContain('\r', p.German + p.Russian);
                Assert.DoesNotContain('\t', p.German + p.Russian);
            });
        }
    }

    [Fact]
    public void Load_SkipsMissingFile_AndToleratesCrlfCommentsAndBlankLines()
    {
        var files = new Dictionary<string, string>
        {
            ["work.tsv"] = "# Begrüßung\r\nGuten Morgen\tДоброе утро\r\n\r\nTschüss\tПока\r\n",
            ["doctor.tsv"] = "Ich bin krank\tЯ болен",
        };

        var seed = SeedData.Load(name => files.GetValueOrDefault(name));

        Assert.Equal(new[] { "Работа", "Врач" }, seed.Select(s => s.TopicName));
        Assert.Equal(new[] { ("Guten Morgen", "Доброе утро"), ("Tschüss", "Пока") }, seed[0].Phrases);
    }

    [Fact]
    public void EnsureSeeded_FillsEmptyDb_WithBuiltInEnabledTopics()
    {
        SeedData.EnsureSeeded(Repo);

        var topics = Repo.GetTopics();
        Assert.Equal(TopicOrder, topics.Select(t => t.Name));
        Assert.All(topics, t =>
        {
            Assert.True(t.IsBuiltIn);
            Assert.True(t.IsEnabled);
            Assert.Equal(ExpectedCategory(t.Name), t.Category);
        });
        Assert.Equal(SeedData.Load().Sum(s => s.Phrases.Count), Repo.GetPhrases().Count);
    }

    [Fact]
    public void EnsureSeeded_DoesNothing_WhenAnyTopicExists()
    {
        Repo.AddTopic("Моя тема", null);
        SeedData.EnsureSeeded(Repo);
        Assert.Single(Repo.GetTopics());
        Assert.Empty(Repo.GetPhrases());
    }

    [Fact]
    public void RestoreMissing_ReturnsDeletedPhrase_WithoutDuplicates_AndKeepsUserPhrases()
    {
        SeedData.EnsureSeeded(Repo);
        var work = Repo.GetTopics().Single(t => t.Name == "Работа");
        var victim = Repo.GetPhrases(work.Id)[0];
        Repo.DeletePhrases([victim.Id]);
        var mine = Repo.AddPhrase(work.Id, "Meine eigene Phrase", "Моя фраза");
        var total = Repo.GetPhrases().Count;

        Assert.Equal(1, SeedData.RestoreMissing(Repo));
        Assert.Equal(total + 1, Repo.GetPhrases().Count);
        Assert.Contains(Repo.GetPhrases(work.Id), p => ImportParser.Normalize(p.German) == ImportParser.Normalize(victim.German));
        Assert.NotNull(Repo.GetPhrase(mine.Id));

        Assert.Equal(0, SeedData.RestoreMissing(Repo));
    }

    [Fact]
    public void RestoreMissing_RenamedBuiltInTopic_GivesNoDuplicates()
    {
        SeedData.EnsureSeeded(Repo);
        var work = Repo.GetTopics().Single(t => t.Name == "Работа");
        Repo.RenameTopic(work.Id, "Моя работа", work.Category);
        var total = Repo.GetPhrases().Count;

        Assert.Equal(0, SeedData.RestoreMissing(Repo));
        Assert.Equal(total, Repo.GetPhrases().Count);
        Assert.DoesNotContain(Repo.GetTopics(), t => t.Name == "Работа");
    }

    [Fact]
    public void RestoreMissing_PhraseMovedToUserTopic_GivesNoDuplicate()
    {
        SeedData.EnsureSeeded(Repo);
        var work = Repo.GetTopics().Single(t => t.Name == "Работа");
        var moved = Repo.GetPhrases(work.Id)[0];
        var mine = Repo.AddTopic("Моя тема", null);
        Repo.MovePhrases([moved.Id], mine.Id);
        var total = Repo.GetPhrases().Count;

        Assert.Equal(0, SeedData.RestoreMissing(Repo));
        Assert.Equal(total, Repo.GetPhrases().Count);
    }

    [Fact]
    public void RestoreMissing_RecreatesDeletedBuiltInTopic()
    {
        SeedData.EnsureSeeded(Repo);
        var doctor = Repo.GetTopics().Single(t => t.Name == "Врач");
        var count = Repo.GetPhrases(doctor.Id).Count;
        Repo.DeleteTopic(doctor.Id);

        Assert.Equal(count, SeedData.RestoreMissing(Repo));
        var restored = Repo.GetTopics().Single(t => t.Name == "Врач");
        Assert.True(restored.IsBuiltIn);
        Assert.Equal("Базовый набор", restored.Category);
    }

    [Fact]
    public void EnsureSeeded_WithSubstitutedFiles_AssignsCategoryPerTopic()
    {
        var files = new Dictionary<string, string>
        {
            ["work.tsv"] = "Guten Morgen\tДоброе утро",
            ["dev-terms.tsv"] = "der Fehler\tошибка",
        };

        SeedData.EnsureSeeded(Repo, name => files.GetValueOrDefault(name));

        var topics = Repo.GetTopics();
        Assert.Equal(new[] { "Работа", "IT-термины" }, topics.Select(t => t.Name));
        Assert.Equal(new[] { BaseCategory, DevCategory }, topics.Select(t => t.Category));
        Assert.All(topics, t => Assert.True(t.IsBuiltIn));
        Assert.Equal(2, Repo.GetPhrases().Count);
    }

    [Fact]
    public void RestoreMissing_OnDbWithOnlyBaseTopics_AddsDevTopicsWithCategory_WithoutDuplicates()
    {
        var baseFiles = new Dictionary<string, string>
        {
            ["work.tsv"] = "Guten Morgen\tДоброе утро",
            ["doctor.tsv"] = "Ich bin krank\tЯ болен",
        };
        SeedData.EnsureSeeded(Repo, name => baseFiles.GetValueOrDefault(name));
        var allFiles = new Dictionary<string, string>(baseFiles)
        {
            ["dev-terms.tsv"] = "# IT\r\nder Fehler\tошибка\r\ndie Anforderung\tтребование",
            ["dev-bugs.tsv"] = "der Absturz\tсбой",
        };

        Assert.Equal(3, SeedData.RestoreMissing(Repo, name => allFiles.GetValueOrDefault(name)));

        var topics = Repo.GetTopics();
        Assert.Equal(4, topics.Count);
        var terms = topics.Single(t => t.Name == "IT-термины");
        Assert.True(terms.IsBuiltIn);
        Assert.Equal(DevCategory, terms.Category);
        Assert.Equal(DevCategory, topics.Single(t => t.Name == "Баги и инциденты").Category);
        Assert.Equal(2, Repo.GetPhrases(terms.Id).Count);
        Assert.Single(Repo.GetPhrases(topics.Single(t => t.Name == "Работа").Id));
        Assert.Equal(5, Repo.GetPhrases().Count);
        Assert.Equal(0, SeedData.RestoreMissing(Repo, name => allFiles.GetValueOrDefault(name)));
    }
}
