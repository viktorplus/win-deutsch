namespace WinDeutsch.Core.Models;

public sealed record Topic(long Id, string Name, string? Category, bool IsEnabled, bool IsBuiltIn, int SortOrder);

public enum PhraseStatus { Active = 0, Excluded = 1 }

public sealed record Phrase(
    long Id, long TopicId, string German, string Russian,
    PhraseStatus Status,
    int Level,                // 0..5
    bool RepeatMore,          // "Повторить чаще" flag
    DateTimeOffset? SnoozedUntil,
    int ShowCount, int CorrectCount, int WrongCount,
    DateTimeOffset? LastShownAt,
    DateTimeOffset CreatedAt);

// Derived: ShowCount==0 -> New; level 0-1 Unknown; 2-4 Learning; 5 Known
public enum Familiarity { New, Unknown, Learning, Known }

public enum ShowOutcome { Shown, Correct, Wrong, Known, Unknown, Later, Excluded, RepeatMore }

public enum DisplayMode { Widget, Popup }

public enum TranslationMode { Immediate, OnClick, Delayed, Never }

public sealed record ShowEvent(long Id, long PhraseId, DateTimeOffset At, DisplayMode Mode, ShowOutcome Outcome);
