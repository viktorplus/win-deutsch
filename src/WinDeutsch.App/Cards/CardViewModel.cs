using System.ComponentModel;
using System.Windows.Media;
using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.App.Cards;

public sealed record QuizOption(int Index, string Text, Brush Background);

// State of the one card shown in the widget or the popup. Every change re-raises all derived properties.
public sealed class CardViewModel : INotifyPropertyChanged
{
    private static readonly Brush CorrectBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x3E)));
    private static readonly Brush WrongBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0x30, 0x30)));
    private static readonly Brush OptionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)));

    private string _german = "";
    private string _russian = "";
    private bool _isTranslationVisible;
    private TranslationMode _translationMode;
    private Quiz? _quiz;
    private int? _quizAnswerIndex;
    private bool _isPaused;
    private string _pausedText = "";
    private string? _emptyText;
    private bool _canGoBack;
    private Brush _textBrush = Brushes.White;
    private Brush _backgroundBrush = Brushes.Black;
    private FontFamily _fontFamily = new("Segoe UI");
    private double _fontSize = 18;
    private bool _controlsOnHover = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string German { get => _german; set => Set(ref _german, value); }
    public string Russian { get => _russian; set => Set(ref _russian, value); }
    public bool IsTranslationVisible { get => _isTranslationVisible; set => Set(ref _isTranslationVisible, value); }
    public TranslationMode TranslationMode { get => _translationMode; set => Set(ref _translationMode, value); }
    public Quiz? Quiz { get => _quiz; set => Set(ref _quiz, value); }
    public int? QuizAnswerIndex { get => _quizAnswerIndex; set => Set(ref _quizAnswerIndex, value); }
    public bool IsPaused { get => _isPaused; set => Set(ref _isPaused, value); }
    public string PausedText { get => _pausedText; set => Set(ref _pausedText, value); }
    public string? EmptyText { get => _emptyText; set => Set(ref _emptyText, value); }
    public bool CanGoBack { get => _canGoBack; set => Set(ref _canGoBack, value); }

    // Appearance (shared by widget and popup).
    public Brush TextBrush { get => _textBrush; private set => Set(ref _textBrush, value); }
    public Brush BackgroundBrush { get => _backgroundBrush; private set => Set(ref _backgroundBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => Set(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => Set(ref _fontSize, value); }
    public double TranslationFontSize => Math.Round(FontSize * 0.8, 1);
    public double SmallFontSize => Math.Max(10, Math.Round(FontSize * 0.65, 1));
    public bool ControlsOnHover { get => _controlsOnHover; private set => Set(ref _controlsOnHover, value); }

    // Derived view states: exactly one of paused / empty / quiz / phrase is shown.
    public bool ShowPaused => IsPaused;
    public bool ShowEmpty => !IsPaused && EmptyText is not null;
    public bool ShowQuiz => !IsPaused && EmptyText is null && Quiz is not null;
    public bool ShowPhrase => !IsPaused && EmptyText is null && Quiz is null;
    public bool ShowTranslation => ShowPhrase && IsTranslationVisible && TranslationMode != TranslationMode.Never && Russian.Length > 0;
    public bool CanReveal => ShowPhrase && !ShowTranslation && TranslationMode != TranslationMode.Never && Russian.Length > 0;
    public bool HasPhrase => ShowPhrase || ShowQuiz;
    public bool IsQuizAnswered => QuizAnswerIndex is not null;

    public IReadOnlyList<QuizOption> QuizOptions => Quiz is null
        ? []
        : Quiz.Options.Select((text, i) => new QuizOption(i, text, OptionBackground(i))).ToList();

    public void ApplyAppearance(AppearanceSettings a)
    {
        var text = ParseColor(a.TextColor, Color.FromRgb(0xF2, 0xF2, 0xF2));
        var back = ParseColor(a.BackgroundColor, Color.FromRgb(0x20, 0x20, 0x20));
        back.A = (byte)Math.Round(Math.Clamp(a.Opacity, 20, 100) * 255 / 100.0);
        TextBrush = Freeze(new SolidColorBrush(text));
        BackgroundBrush = Freeze(new SolidColorBrush(back));
        FontFamily = new FontFamily(string.IsNullOrWhiteSpace(a.FontFamily) ? "Segoe UI" : a.FontFamily);
        FontSize = Math.Clamp(a.FontSize, 10, 48);
        ControlsOnHover = a.ControlsOnHover;
    }

    private Brush OptionBackground(int index)
    {
        if (QuizAnswerIndex is not { } answer || Quiz is null)
            return OptionBrush;
        if (index == Quiz.CorrectIndex)
            return CorrectBrush;
        return index == answer ? WrongBrush : OptionBrush;
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            return hex is null ? fallback : (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        // Empty name = all properties: the derived states depend on several fields.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
