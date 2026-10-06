using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Tests.Learning;

public sealed class QuizBuilderTests
{
    private static Phrase P(long id, long topic, string russian) =>
        LearningRulesTests.Make(id: id, topicId: topic, german: $"de{id}", russian: russian);

    private static readonly Phrase Target = P(1, 1, "Доброе утро");   // 11 chars -> close = 6..16

    [Fact]
    public void Build_TargetWithoutTranslation_ReturnsNull()
    {
        var pool = new[] { P(2, 1, "Спасибо"), P(3, 1, "Пока"), P(4, 1, "Привет") };
        Assert.Null(new QuizBuilder(new Random(1)).Build(P(9, 1, "  "), pool));
    }

    [Fact]
    public void Build_LessThanThreeDistinctDistractors_ReturnsNull()
    {
        // Same text as the answer, duplicates, empty and the target itself do not count.
        var pool = new[]
        {
            Target, P(2, 1, "Доброе утро"), P(3, 1, "Спасибо"), P(4, 2, "спасибо "), P(5, 1, ""), P(6, 2, "Пока"),
        };
        Assert.Null(new QuizBuilder(new Random(1)).Build(Target, pool));
    }

    [Fact]
    public void Build_GivesFourDistinctOptions_WithCorrectAtCorrectIndex()
    {
        var pool = new[] { Target, P(2, 1, "Спасибо"), P(3, 2, "Пока"), P(4, 1, "Привет"), P(5, 2, "Добрый вечер") };
        var quiz = new QuizBuilder(new Random(1)).Build(Target, pool)!;

        Assert.Same(Target, quiz.Target);
        Assert.Equal(4, quiz.Options.Count);
        Assert.Equal(4, quiz.Options.Distinct().Count());
        Assert.Equal("Доброе утро", quiz.Options[quiz.CorrectIndex]);
    }

    [Fact]
    public void Build_CorrectPosition_IsRandom()
    {
        var pool = new[] { Target, P(2, 1, "Спасибо"), P(3, 2, "Пока"), P(4, 1, "Привет"), P(5, 2, "Добрый вечер") };
        var builder = new QuizBuilder(new Random(7));
        var positions = Enumerable.Range(0, 200).Select(_ => builder.Build(Target, pool)!.CorrectIndex).ToHashSet();
        Assert.Equal(new[] { 0, 1, 2, 3 }, positions.Order());
    }

    [Fact]
    public void Build_PrefersSameTopicCloseLength_ThenOtherTopics()
    {
        var pool = new List<Phrase>
        {
            Target,
            P(2, 1, "Добрый день"),          // same topic, close
            P(3, 1, "Добрый вечер"),         // same topic, close
            P(4, 1, "Да"),                   // same topic, too short
            P(5, 1, "Очень длинный перевод, который явно не подходит по длине"), // same topic, too long
        };
        for (var i = 0; i < 20; i++)
            pool.Add(P(100 + i, 2, $"Другая тема {i}"));   // other topic, close length

        var builder = new QuizBuilder(new Random(11));
        for (var i = 0; i < 100; i++)
        {
            var options = builder.Build(Target, pool)!.Options;
            Assert.Contains("Добрый день", options);
            Assert.Contains("Добрый вечер", options);
            // The third distractor comes from other close-length phrases before same-topic far-length ones.
            Assert.DoesNotContain("Да", options);
            Assert.Single(options, o => o.StartsWith("Другая тема", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Build_FallsBackToAnyLength_WhenCloseOnesRunOut()
    {
        var pool = new[] { Target, P(2, 1, "Да"), P(3, 2, "Нет"), P(4, 2, "Очень длинный перевод, который не подходит по длине") };
        var quiz = new QuizBuilder(new Random(3)).Build(Target, pool)!;
        Assert.Equal(4, quiz.Options.Count);
        Assert.Contains("Да", quiz.Options);
        Assert.Contains("Нет", quiz.Options);
    }
}
