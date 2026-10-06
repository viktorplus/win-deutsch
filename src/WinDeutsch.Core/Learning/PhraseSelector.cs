using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Learning;

public sealed class PhraseSelector
{
    private readonly Random _random;

    public PhraseSelector(Random random) => _random = random;

    // Weighted random pick (LearningRules.Weight). recentIds is most recent first; the last
    // min(10, candidates/2) of them are skipped. A single candidate is always returned.
    public Phrase? Pick(IReadOnlyList<Phrase> candidates, IReadOnlyList<long> recentIds)
    {
        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();

        var skip = recentIds.Take(Math.Min(10, candidates.Count / 2)).ToHashSet();
        var allowed = candidates.Where(c => !skip.Contains(c.Id)).ToList();

        var total = allowed.Sum(LearningRules.Weight);
        var point = _random.NextDouble() * total;
        foreach (var phrase in allowed)
        {
            point -= LearningRules.Weight(phrase);
            if (point < 0)
                return phrase;
        }
        return allowed[^1];   // floating-point leftover
    }
}
