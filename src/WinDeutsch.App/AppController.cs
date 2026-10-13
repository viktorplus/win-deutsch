using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using WinDeutsch.App.Cards;
using WinDeutsch.App.Platform;
using WinDeutsch.App.Tray;
using WinDeutsch.App.Windows;
using WinDeutsch.Core;
using WinDeutsch.Core.Backup;
using WinDeutsch.Core.Import;
using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;
using WinDeutsch.Core.Seed;
using WinDeutsch.Core.Settings;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.App;

// The single owner of data, settings, scheduling and windows. Everything runs on the UI thread.
public sealed class AppController
{
    public const string EmptyPoolText = "Нет фраз для показа — включите тему в библиотеке";

    private const int HistoryLimit = 50;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADb = 26;
    private static readonly TimeSpan EnvironmentCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QuizFeedback = TimeSpan.FromSeconds(2.5);

    private readonly TimeProvider _time = TimeProvider.System;
    private readonly Random _random = new();
    private readonly SettingsStore _store;
    private readonly ShowScheduler _scheduler;
    private readonly PhraseSelector _selector;
    private readonly QuizBuilder _quizBuilder;
    private readonly DispatcherTimer _timer;
    private readonly List<long> _history = new();   // shown phrase ids, oldest first
    private readonly List<string> _notices = new();  // startup messages for the one tray balloon

    private WidgetWindow? _widget;
    private PopupWindow? _popup;
    private TrayIcon? _tray;
    private IdleDetector? _idle;
    private HotkeyManager? _hotkeys;
    private LibraryWindow? _library;
    private SettingsWindow? _settingsWindow;

    private Phrase? _current;
    private int _historyPos = -1;
    private DateTimeOffset? _revealAt;
    private int _popupSecondsLeft;
    private int _displayVersion;                     // bumps on every display; stale quiz feedback is ignored
    private bool _suppressed;                        // full screen app or quiet hours: widget hidden, no shows
    private bool _away;                              // idle or locked: nothing changes
    private DateTimeOffset _lastEnvironmentCheck = DateTimeOffset.MinValue;
    private bool _placementDirty;
    private DisplayMode _appliedMode;
    private (IntervalKind, int, int, int) _appliedInterval;

    public AppController()
    {
        var firstRun = !File.Exists(AppPaths.SettingsFile);
        _store = new SettingsStore(AppPaths.SettingsFile);
        Settings = LoadSettings();
        Repository = OpenRepository();
        ConnectNewDictionaries();
        if (firstRun)
            _notices.Add("Win Deutsch работает — значок в трее");

        _scheduler = new ShowScheduler(_time, _random);
        _selector = new PhraseSelector(_random);
        _quizBuilder = new QuizBuilder(_random);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public AppSettings Settings { get; private set; }
    public PhraseRepository Repository { get; private set; }
    public CardViewModel Card { get; } = new();
    public long? CurrentPhraseId => _current?.Id;
    public IReadOnlyList<string> FailedHotkeys => _hotkeys?.FailedHotkeys ?? [];

    public bool IsPaused => Settings.PausedUntil is { } until && until > _time.GetUtcNow();

    public event EventHandler? StateChanged;

    public void Start()
    {
        _widget = new WidgetWindow(this);
        _widget.PlacementChanged += (_, _) => _placementDirty = true;
        _popup = new PopupWindow(this);
        _tray = new TrayIcon(this);
        _idle = new IdleDetector();
        _hotkeys = new HotkeyManager();

        _appliedMode = Settings.Mode;
        ApplySettings();
        ApplyPauseState();
        UpdateWindows();
        if (Settings.Mode == DisplayMode.Widget && !IsPaused)
            ShowNewPhrase(automatic: true);
        _scheduler.Reset(Settings.Interval);
        _timer.Start();

        if (_notices.Count > 0)
            _tray.ShowBalloon(string.Join(Environment.NewLine, _notices));
        _notices.Clear();
    }

    // ---- actions for windows, tray and hotkeys ----

    public void ApplySettings()
    {
        SaveSettings();   // also clamps the values
        Card.ApplyAppearance(Settings.Appearance);
        _widget?.ApplyLayout(Settings);
        if (_popup is { IsShown: true })
            _popup.Pop(Settings.Popup);

        var interval = (Settings.Interval.Kind, Settings.Interval.Seconds, Settings.Interval.MinSeconds, Settings.Interval.MaxSeconds);
        if (interval != _appliedInterval)
        {
            _appliedInterval = interval;
            _scheduler.Reset(Settings.Interval);
        }
        if (Settings.Mode != _appliedMode)
            ApplyMode();

        RegisterHotkeys();
        try
        {
            if (AutostartRegistry.IsEnabled() != Settings.StartWithWindows)
                AutostartRegistry.Set(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            Log.Error("Autostart registry update failed", ex);
        }
        RaiseStateChanged();
    }

    public void ShowNow()
    {
        if (Settings.Mode == DisplayMode.Widget && Settings.WidgetHidden)
            ToggleWidgetHidden();
        ShowNewPhrase(automatic: false);
    }

    public void Next() => ShowNewPhrase(automatic: false);

    public void Previous()
    {
        if (IsPaused)
            return;   // a pause stays silent until Resume
        while (_historyPos > 0)
        {
            _historyPos--;
            if (Repository.GetPhrase(_history[_historyPos]) is { } phrase)
            {
                _scheduler.Reset(Settings.Interval);
                Display(phrase, quiz: null);
                return;
            }
            _history.RemoveAt(_historyPos);   // deleted meanwhile
        }
    }

    public void RevealTranslation()
    {
        if (Card.TranslationMode == TranslationMode.Never)
            return;
        _revealAt = null;
        Card.IsTranslationVisible = true;
    }

    // Known / Unknown / Later / Excluded / RepeatMore for the current phrase; all but RepeatMore move on.
    public void HandleOutcome(ShowOutcome outcome)
    {
        if (_current is null)
            return;
        if (Repository.GetPhrase(_current.Id) is not { } phrase)
        {
            ShowNewPhrase(automatic: false);
            return;
        }
        _current = Record(phrase, outcome);
        if (outcome == ShowOutcome.RepeatMore)
            RaiseStateChanged();
        else
            ShowNewPhrase(automatic: false);
    }

    // optionIndex -1 = "Не знаю": counted as Unknown, only the right answer is highlighted.
    public void AnswerQuiz(int optionIndex)
    {
        if (_current is null || Card.Quiz is not { } quiz || Card.QuizAnswerIndex is not null)
            return;
        var outcome = optionIndex < 0 ? ShowOutcome.Unknown
            : optionIndex == quiz.CorrectIndex ? ShowOutcome.Correct
            : ShowOutcome.Wrong;
        if (Repository.GetPhrase(_current.Id) is { } phrase)
            _current = Record(phrase, outcome);
        Card.QuizAnswerIndex = optionIndex < 0 ? quiz.CorrectIndex : optionIndex;
        _popupSecondsLeft = Math.Max(_popupSecondsLeft, 4);

        var version = _displayVersion;
        After(QuizFeedback, () =>
        {
            if (version != _displayVersion)
                return;
            Card.Quiz = null;
            Card.QuizAnswerIndex = null;
            Card.IsTranslationVisible = Card.TranslationMode != TranslationMode.Never;
            _widget?.FitContent(quizShown: false);
            if (Settings.Mode == DisplayMode.Popup)
                _popup?.FadeAway();
        });
    }

    public void Pause(PauseOption option)
    {
        Settings.PausedUntil = ShowScheduler.PauseUntil(option, _time.GetLocalNow());
        SaveSettings();
        ApplyPauseState();
    }

    public void Resume()
    {
        Settings.PausedUntil = null;
        SaveSettings();
        _scheduler.Reset(Settings.Interval);
        ApplyPauseState();
        if (Settings.Mode == DisplayMode.Widget && _current is null)
            ShowNewPhrase(automatic: true);
    }

    public void ToggleWidgetHidden()
    {
        Settings.WidgetHidden = !Settings.WidgetHidden;
        SaveSettings();
        UpdateWindows();
        RaiseStateChanged();
    }

    // ✕ on the card: the widget hides until the tray/hotkey brings it back; the popup just closes.
    public void CloseCard()
    {
        if (Settings.Mode == DisplayMode.Popup)
            _popup?.FadeAway();
        else if (!Settings.WidgetHidden)
            ToggleWidgetHidden();
    }

    public void SetMode(DisplayMode mode)
    {
        if (Settings.Mode == mode)
            return;
        Settings.Mode = mode;
        SaveSettings();
        ApplyMode();
        RaiseStateChanged();
    }

    public void OpenLibrary(long? phraseId = null)
    {
        if (_library is null)
        {
            _library = new LibraryWindow(this);
            _library.Closed += (_, _) => _library = null;
            _library.Show();
        }
        BringToFront(_library);
        if (phraseId is { } id)
            _library.SelectPhrase(id);
    }

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        BringToFront(_settingsWindow);
    }

    // After import / restore / library edits: refresh the current card from the database.
    public void ReloadData()
    {
        var fresh = _current is null ? null : Repository.GetPhrase(_current.Id);
        if (fresh is { Status: PhraseStatus.Active } && !string.IsNullOrWhiteSpace(fresh.German))
        {
            _current = fresh;
            Card.German = fresh.German;
            Card.Russian = fresh.Russian.Trim();
        }
        else
        {
            // Replace the card only where it is on screen; a closed popup just forgets the phrase.
            var onScreen = Settings.Mode == DisplayMode.Widget || (_current is not null && _popup is { IsShown: true });
            _current = null;
            if (onScreen)
                ShowNewPhrase(automatic: true);
        }
        RaiseStateChanged();
    }

    // Replaces data.db and settings.json from a backup zip and applies everything restored without a restart.
    // Throws (InvalidDataException, IOException) before anything changes when the zip is unusable.
    public void RestoreFromBackup(string zipPath)
    {
        // Connections are opened per call (Pooling=false), so none holds the file here.
        BackupService.Restore(zipPath);
        Repository = OpenRepository();
        Settings = LoadSettings();
        _placementDirty = false;
        _history.Clear();
        _historyPos = -1;
        _current = null;

        ApplySettings();
        ApplyPauseState();
        UpdateWindows();
        if (Settings.Mode == DisplayMode.Widget && !IsPaused && _current is null)
            ShowNewPhrase(automatic: true);
        if (_notices.Count > 0)
        {
            _tray?.ShowBalloon(string.Join(Environment.NewLine, _notices));
            _notices.Clear();
        }
    }

    public void Exit()
    {
        _timer.Stop();
        if (_placementDirty)
            SaveSettings();
        _hotkeys?.Dispose();
        _idle?.Dispose();
        _tray?.Dispose();
        foreach (var window in Application.Current.Windows.Cast<Window>().ToList())
            window.Close();
        Application.Current.Shutdown();
    }

    // ---- showing ----

    private void ShowNewPhrase(bool automatic)
    {
        if (IsPaused)
            return;   // only Resume ends the silence: no Shown events, no popups
        _scheduler.Reset(Settings.Interval);
        var recent = Enumerable.Reverse(_history).ToList();
        var picked = _selector.Pick(Repository.GetCandidates(), recent);
        if (picked is null)
        {
            _current = null;
            _displayVersion++;
            _revealAt = null;
            Card.Quiz = null;
            Card.EmptyText = EmptyPoolText;
            Card.CanGoBack = _historyPos > 0;
            // No popups for an empty pool; an explicit request still shows why.
            if (!automatic || Settings.Mode == DisplayMode.Widget)
                Present(quizShown: false);
            return;
        }

        var phrase = Record(picked, ShowOutcome.Shown);
        _history.Add(phrase.Id);
        if (_history.Count > HistoryLimit)
            _history.RemoveAt(0);
        _historyPos = _history.Count - 1;

        Quiz? quiz = null;
        if (Settings.QuizEnabled && _random.Next(100) < Settings.QuizPercent)
            quiz = _quizBuilder.Build(phrase, QuizPool());
        Display(phrase, quiz);
    }

    private void Display(Phrase phrase, Quiz? quiz)
    {
        _current = phrase;
        _displayVersion++;
        var mode = Settings.Mode == DisplayMode.Widget ? Settings.WidgetTranslation : Settings.PopupTranslation;
        _revealAt = mode == TranslationMode.Delayed ? _time.GetUtcNow().AddSeconds(Settings.TranslationDelaySeconds) : null;

        Card.EmptyText = null;
        Card.German = phrase.German;
        Card.Russian = phrase.Russian.Trim();
        Card.TranslationMode = mode;
        Card.IsTranslationVisible = mode == TranslationMode.Immediate;
        Card.QuizAnswerIndex = null;
        Card.Quiz = quiz;
        Card.CanGoBack = _historyPos > 0;
        Present(quiz is not null);
    }

    private void Present(bool quizShown)
    {
        if (Settings.Mode == DisplayMode.Widget)
        {
            _widget?.FitContent(quizShown);
        }
        else if (_popup is not null)
        {
            // A quiz waits for the answer, but not longer than max(N, 60) s; an unanswered one records nothing.
            _popupSecondsLeft = quizShown ? Math.Max(Settings.PopupDurationSeconds, 60) : Settings.PopupDurationSeconds;
            _popup.Pop(Settings.Popup);
        }
        RaiseStateChanged();
    }

    private Phrase Record(Phrase phrase, ShowOutcome outcome)
    {
        var updated = LearningRules.Apply(phrase, outcome, _time.GetUtcNow());
        Repository.SaveLearningState(updated);
        Repository.AddEvent(updated.Id, Settings.Mode, outcome);
        return updated;
    }

    private IReadOnlyList<Phrase> QuizPool() =>
        Repository.GetPhrases().Where(p => p.Status == PhraseStatus.Active && !string.IsNullOrWhiteSpace(p.Russian)).ToList();

    // ---- the one timer ----

    private void OnTick()
    {
        var now = _time.GetUtcNow();
        if (Settings.PausedUntil is { } until && until <= now)
            Resume();
        if (_placementDirty)
        {
            _placementDirty = false;
            SaveSettings();
        }
        if (_revealAt is { } revealAt && now >= revealAt)
            RevealTranslation();
        if (_popup is { IsShown: true, IsMouseOver: false } && --_popupSecondsLeft <= 0)
            _popup.FadeAway();

        if (IsPaused)
            return;
        var widgetVisible = Settings.Mode == DisplayMode.Widget && !Settings.WidgetHidden;
        // Popup mode checks every time too: quiet hours / idle / lock that start and end between shows must reset the interval.
        var needsCheck = widgetVisible || Settings.Mode == DisplayMode.Popup || _scheduler.IsDue();
        if (needsCheck && now - _lastEnvironmentCheck >= EnvironmentCheckInterval)
            CheckEnvironment(now);
        // Re-read: coming back from idle/lock/full screen/quiet hours resets the interval in CheckEnvironment.
        if (!_scheduler.IsDue() || _suppressed || _away)
            return;

        if (Settings.Mode == DisplayMode.Widget && Settings.WidgetHidden)
            _scheduler.Reset(Settings.Interval);
        else if (Settings.Mode == DisplayMode.Widget || _popup is not { IsShown: true })
            ShowNewPhrase(automatic: true);
    }

    private void CheckEnvironment(DateTimeOffset now)
    {
        _lastEnvironmentCheck = now;
        var localTime = TimeOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var suppressed = ShowScheduler.IsInQuietHours(localTime, Settings.QuietHours)
                         || (Settings.HideOnFullscreen && FullscreenDetector.IsFullscreenAppActive());
        var away = _idle is { IsSessionLocked: true }
                   || (Settings.PauseWhenIdle && IdleDetector.IdleTime >= TimeSpan.FromMinutes(Math.Max(1, Settings.IdleMinutes)));

        if ((_suppressed || _away) && !suppressed && !away)
            _scheduler.Reset(Settings.Interval);   // back at the screen: count the interval from scratch
        _away = away;
        if (suppressed != _suppressed)
        {
            _suppressed = suppressed;
            UpdateWindows();
        }
    }

    // ---- helpers ----

    private void ApplyMode()
    {
        _appliedMode = Settings.Mode;
        UpdateWindows();
        _scheduler.Reset(Settings.Interval);
        if (Settings.Mode == DisplayMode.Widget && _current is null)
            ShowNewPhrase(automatic: true);
    }

    private void UpdateWindows()
    {
        var showWidget = Settings.Mode == DisplayMode.Widget && !Settings.WidgetHidden && !_suppressed;
        if (_widget is not null && showWidget != _widget.IsVisible)
        {
            if (showWidget)
                _widget.Show();
            else
                _widget.Hide();
        }
        if (_popup is { IsShown: true } && (Settings.Mode != DisplayMode.Popup || _suppressed))
            _popup.FadeAway();
    }

    private void ApplyPauseState()
    {
        Card.IsPaused = IsPaused;
        Card.PausedText = PausedText();
        if (IsPaused)
            _popup?.FadeAway();
        RaiseStateChanged();
    }

    private string PausedText()
    {
        if (Settings.PausedUntil is not { } until || !IsPaused)
            return "";
        if (until.Year >= 9999)
            return "Пауза";
        var local = until.ToLocalTime();
        var today = _time.GetLocalNow().Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return local.Date == today ? $"Пауза до {time}"
            : local.Date == today.AddDays(1) ? $"Пауза до завтра, {time}"
            : $"Пауза до {local.ToString("dd.MM", CultureInfo.InvariantCulture)} {time}";
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null)
            return;
        if (!Settings.HotkeysEnabled)
        {
            _hotkeys.UnregisterAll();
            return;
        }
        _hotkeys.Register(
        [
            ("Ctrl+Alt+→", 0x27, Next),
            ("Ctrl+Alt+←", 0x25, Previous),
            ("Ctrl+Alt+Пробел", 0x20, RevealTranslation),
            ("Ctrl+Alt+P", 0x50, () =>
            {
                if (IsPaused)
                    Resume();
                else
                    Pause(PauseOption.Indefinite);
            }),
            ("Ctrl+Alt+H", 0x48, () =>
            {
                if (Settings.Mode == DisplayMode.Widget)
                    ToggleWidgetHidden();
                else
                    CloseCard();
            }),
        ]);
    }

    private AppSettings LoadSettings()
    {
        try
        {
            var settings = _store.Load();
            if (_store.LastLoadWasBroken)
                _notices.Add($"Настройки были повреждены — включены настройки по умолчанию. Старый файл: {Path.GetFileName(_store.BrokenFilePath)}");
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be loaded", ex);
            _notices.Add("Не удалось прочитать настройки — включены настройки по умолчанию.");
            return new AppSettings();
        }
    }

    // A database that is not SQLite or is damaged is set aside and replaced by a fresh one with the base set.
    private PhraseRepository OpenRepository()
    {
        var path = AppPaths.DatabaseFile;
        try
        {
            return OpenSeeded(path);
        }
        catch (Exception ex) when (ex is DatabaseCorruptException || ex is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADb })
        {
            Log.Error("Database could not be opened", ex);
            var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var broken = $"{path}.broken-{stamp}";
            File.Move(path, broken, overwrite: true);
            _notices.Add($"База данных повреждена и сохранена как {Path.GetFileName(broken)}. Создана новая база с базовым набором.");
            return OpenSeeded(path);
        }
    }

    // Dictionaries next to the exe that were never connected are imported once and remembered in the settings.
    private void ConnectNewDictionaries()
    {
        var added = DictionaryFolder.ImportNew(Repository, DictionaryFolder.Default, Settings.ImportedDictionaries,
            (path, ex) => Log.Error($"Dictionary could not be imported: {path}", ex));
        if (added.Count == 0)
            return;
        Settings.ImportedDictionaries.AddRange(added);
        SaveSettings();
    }

    private PhraseRepository OpenSeeded(string path)
    {
        var repository = new PhraseRepository(new Database(path), _time);
        SeedData.EnsureSeeded(repository);
        return repository;
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Settings could not be saved", ex);
        }
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
