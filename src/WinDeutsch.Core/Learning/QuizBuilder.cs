using WinDeutsch.Core.Models;

namespace WinDeutsch.Core.Learning;

public sealed record Quiz(Phrase Target, IReadOnlyList<string> Options, int CorrectIndex);

public sealed class QuizBuilder
{
    private const int Distractors = 3;

    private readonly Random _random;

    public QuizBuilder(Random random) => _random = random;

    // Distractors are other phrases' translations, distinct and different from the answer, taken in this order:
    // same topic and close length (±50 % chars), any topic and close length, the rest. Random within each group.
    // Null when the target has no translation or fewer than 3 distractors exist.
    public Quiz? Build(Phrase target, IReadOnlyList<Phrase> pool)
    {
        var answer = target.Russian.Trim();
        if (answer.Length == 0)
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { answer };
        var distractors = pool
            .Where(p => p.Id != target.Id)
            .Select(p => (Phrase: p, Text: p.Russian.Trim()))
            .Where(x => x.Text.Length > 0)
            .OrderBy(x => Rank(x.Phrase, x.Text))
            .ThenBy(_ => _random.Next())
            .Where(x => seen.Add(x.Text))
            .Take(Distractors)
            .Select(x => x.Text)
            .ToList();
        if (distractors.Count < Distractors)
            return null;

        var correctIndex = _random.Next(Distractors + 1);
        distractors.Insert(correctIndex, answer);
        return new Quiz(target, distractors, correctIndex);

        int Rank(Phrase p, string text)
        {
            var close = Math.Abs(text.Length - answer.Length) <= answer.Length * 0.5;
            return close ? (p.TopicId == target.TopicId ? 0 : 1) : 2;
        }
    }
}
