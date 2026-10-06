using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;

namespace WinDeutsch.App.Windows.Library;

// One row of the phrase grid; texts are computed once per reload.
internal sealed class PhraseRow
{
    public PhraseRow(Phrase phrase, string topicName, DateTimeOffset now)
    {
        Phrase = phrase;
        TopicName = topicName;
        State = LibraryText.Familiarity(LearningRules.GetFamiliarity(phrase));
        LastShownSort = phrase.LastShownAt ?? DateTimeOffset.MinValue;
        LastShown = phrase.LastShownAt is { } shown ? LibraryText.Local(shown) : "";

        var notes = new List<string>();
        if (phrase.Status == PhraseStatus.Excluded)
            notes.Add("исключена");
        else if (phrase.SnoozedUntil is { } until && until > now)
            notes.Add("отложена до " + LibraryText.Local(until));
        if (phrase.RepeatMore)
            notes.Add("повторять чаще");
        Note = string.Join("; ", notes);
    }

    public Phrase Phrase { get; }
    public long Id => Phrase.Id;
    public string German => Phrase.German;
    public string Russian => Phrase.Russian;
    public string TopicName { get; }
    public string State { get; }
    public int ShowCount => Phrase.ShowCount;
    public string LastShown { get; }
    public DateTimeOffset LastShownSort { get; }
    public string Note { get; }
}
