using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Tests.Learning;

public sealed class PhraseSelectorTests
{
    private static Phrase P(long id, int level = 2) => LearningRulesTests.Make(level: level, id: id);

    [Fact]
    public void Pick_Empty_ReturnsNull() =>
        Assert.Null(new PhraseSelector(new Random(1)).Pick([], []));

    [Fact]
    public void Pick_SingleCandidate_IsAlwaysReturned_EvenIfJustShown()
    {
        var only = P(7);
        var selector = new PhraseSelector(new Random(1));
        for (var i = 0; i < 20; i++)
            Assert.Same(only, selector.Pick([only], [7]));
    }

    [Fact]
    public void Pick_Level0_IsChosenAtLeast6TimesMoreOftenThanLevel5()
    {
        var unknown = P(1, level: 0);
        var known = P(2, level: 5);
        var selector = new PhraseSelector(new Random(42));
        int u = 0, k = 0;
        for (var i = 0; i < 10_000; i++)
        {
            if (selector.Pick([unknown, known], [])!.Id == 1) u++;
            else k++;
        }
        Assert.True(k > 0, "known phrase must still appear");
        Assert.True(u >= 6 * k, $"unknown={u} known={k}");
    }

    [Fact]
    public void Pick_SkipsLastTenRecent_WhenManyCandidates()
    {
        var candidates = Enumerable.Range(1, 30).Select(i => P(i)).ToList();
        var recent = Enumerable.Range(1, 10).Select(i => (long)i).ToList();
        var selector = new PhraseSelector(new Random(3));
        for (var i = 0; i < 2_000; i++)
            Assert.DoesNotContain(selector.Pick(candidates, recent)!.Id, recent);
    }

    [Fact]
    public void Pick_SkipsOnlyHalfOfCandidates_WhenFew()
    {
        // 5 candidates -> min(10, 5/2) = 2 most recent excluded; the 3rd most recent is allowed.
        var candidates = Enumerable.Range(1, 5).Select(i => P(i)).ToList();
        var recent = new List<long> { 5, 4, 3, 2, 1 };
        var selector = new PhraseSelector(new Random(5));
        var seen = new HashSet<long>();
        for (var i = 0; i < 2_000; i++)
            seen.Add(selector.Pick(candidates, recent)!.Id);
        Assert.Equal(new long[] { 1, 2, 3 }, seen.Order());
    }

    [Fact]
    public void Pick_TwoCandidates_NeverRepeatsTheLastOne()
    {
        var a = P(1, level: 5);
        var b = P(2, level: 0);
        var selector = new PhraseSelector(new Random(9));
        for (var i = 0; i < 200; i++)
            Assert.Equal(1, selector.Pick([a, b], [2])!.Id);
    }
}
