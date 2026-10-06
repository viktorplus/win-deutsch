using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Tests.Learning;

public sealed class LearningRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    internal static Phrase Make(int level = 2, int shows = 3, bool repeatMore = false, long id = 1, long topicId = 1,
        string german = "Guten Morgen", string russian = "Доброе утро") =>
        new(id, topicId, german, russian, PhraseStatus.Active, level, repeatMore, null, shows, 1, 1, null, Now.AddDays(-1));

    [Theory]
    [InlineData(0, 8.0)]
    [InlineData(1, 6.0)]
    [InlineData(2, 4.0)]
    [InlineData(3, 2.0)]
    [InlineData(4, 1.0)]
    [InlineData(5, 0.5)]
    public void Weight_FollowsLevelTable(int level, double expected) =>
        Assert.Equal(expected, LearningRules.Weight(Make(level: level)));

    [Fact]
    public void Weight_NewPhraseIs8_AndRepeatMoreDoubles()
    {
        Assert.Equal(8.0, LearningRules.Weight(Make(level: 0, shows: 0)));
        Assert.Equal(4.0, LearningRules.Weight(Make(level: 3, repeatMore: true)));
    }

    // D01: a manual level on a never-shown phrase counts at once.
    [Theory]
    [InlineData(5, 0.5, Familiarity.Known)]
    [InlineData(3, 2.0, Familiarity.Learning)]
    [InlineData(1, 6.0, Familiarity.Unknown)]
    public void NeverShownWithManualLevel_UsesLevel(int level, double weight, Familiarity familiarity)
    {
        var p = Make(level: level, shows: 0);
        Assert.Equal(weight, LearningRules.Weight(p));
        Assert.Equal(familiarity, LearningRules.GetFamiliarity(p));
    }

    [Theory]
    [InlineData(0, 0, Familiarity.New)]
    [InlineData(0, 1, Familiarity.Unknown)]
    [InlineData(1, 4, Familiarity.Unknown)]
    [InlineData(2, 4, Familiarity.Learning)]
    [InlineData(4, 4, Familiarity.Learning)]
    [InlineData(5, 4, Familiarity.Known)]
    public void GetFamiliarity_FollowsSpecTable(int level, int shows, Familiarity expected) =>
        Assert.Equal(expected, LearningRules.GetFamiliarity(Make(level: level, shows: shows)));

    [Fact]
    public void Apply_Shown_CountsAndStampsTime()
    {
        var r = LearningRules.Apply(Make(shows: 3), ShowOutcome.Shown, Now);
        Assert.Equal(4, r.ShowCount);
        Assert.Equal(Now, r.LastShownAt);
        Assert.Equal(2, r.Level);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    public void Apply_Correct_RaisesLevelCappedAt5_AndClearsRepeatMore(int level, int expected)
    {
        var r = LearningRules.Apply(Make(level: level, repeatMore: true), ShowOutcome.Correct, Now);
        Assert.Equal(expected, r.Level);
        Assert.Equal(2, r.CorrectCount);
        Assert.False(r.RepeatMore);
        Assert.Equal(1, r.WrongCount);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Apply_Wrong_LowersLevelBy2FlooredAt0(int level, int expected)
    {
        var r = LearningRules.Apply(Make(level: level, repeatMore: true), ShowOutcome.Wrong, Now);
        Assert.Equal(expected, r.Level);
        Assert.Equal(2, r.WrongCount);
        Assert.Equal(1, r.CorrectCount);
        Assert.True(r.RepeatMore);
    }

    [Fact]
    public void Apply_Known_SetsLevel5_AndClearsRepeatMore()
    {
        var r = LearningRules.Apply(Make(level: 0, repeatMore: true), ShowOutcome.Known, Now);
        Assert.Equal(5, r.Level);
        Assert.False(r.RepeatMore);
    }

    [Fact]
    public void Apply_Unknown_SetsLevel0()
    {
        var r = LearningRules.Apply(Make(level: 5), ShowOutcome.Unknown, Now);
        Assert.Equal(0, r.Level);
        Assert.Equal(3, r.ShowCount);
    }

    [Fact]
    public void Apply_Later_SnoozesForOneHour()
    {
        var r = LearningRules.Apply(Make(), ShowOutcome.Later, Now);
        Assert.Equal(Now.AddHours(1), r.SnoozedUntil);
        Assert.Equal(2, r.Level);
    }

    [Fact]
    public void Apply_Excluded_SetsStatus()
    {
        var r = LearningRules.Apply(Make(), ShowOutcome.Excluded, Now);
        Assert.Equal(PhraseStatus.Excluded, r.Status);
        Assert.Equal(2, r.Level);
    }

    [Fact]
    public void Apply_RepeatMore_SetsLevel0AndFlag()
    {
        var r = LearningRules.Apply(Make(level: 4), ShowOutcome.RepeatMore, Now);
        Assert.Equal(0, r.Level);
        Assert.True(r.RepeatMore);
    }
}
