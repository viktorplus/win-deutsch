using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Learning;

public static class LearningRules
{
    public const int MaxLevel = 5;

    private static readonly double[] LevelWeights = [8, 6, 4, 2, 1, 0.5];

    // Level 0..5 -> 8,6,4,2,1,0.5 (New is level 0, so 8; a manual level counts even before the first show);
    // "repeat more" doubles it.
    public static double Weight(Phrase p)
    {
        var weight = LevelWeights[Math.Clamp(p.Level, 0, MaxLevel)];
        return p.RepeatMore ? weight * 2 : weight;
    }

    // Never shown and level 0 -> New; level 0-1 Unknown; 2-4 Learning; 5 Known.
    public static Familiarity GetFamiliarity(Phrase p) => p.ShowCount == 0 && p.Level == 0 ? Familiarity.New : p.Level switch
    {
        <= 1 => Familiarity.Unknown,
        <= 4 => Familiarity.Learning,
        _ => Familiarity.Known,
    };

    public static Phrase Apply(Phrase p, ShowOutcome outcome, DateTimeOffset now) => outcome switch
    {
        ShowOutcome.Shown => p with { ShowCount = p.ShowCount + 1, LastShownAt = now },
        ShowOutcome.Correct => p with { Level = Math.Min(p.Level + 1, MaxLevel), CorrectCount = p.CorrectCount + 1, RepeatMore = false },
        ShowOutcome.Wrong => p with { Level = Math.Max(p.Level - 2, 0), WrongCount = p.WrongCount + 1 },
        ShowOutcome.Known => p with { Level = MaxLevel, RepeatMore = false },
        ShowOutcome.Unknown => p with { Level = 0 },
        ShowOutcome.Later => p with { SnoozedUntil = now.AddHours(1) },
        ShowOutcome.Excluded => p with { Status = PhraseStatus.Excluded },
        ShowOutcome.RepeatMore => p with { Level = 0, RepeatMore = true },
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
