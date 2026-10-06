using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinDeutsch.App.Windows.Settings;
using WinDeutsch.Core;
using WinDeutsch.Core.Backup;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.App.Windows;

// Tabs Показ · Тесты · Внешний вид · Поведение · Данные. Every valid change is saved and applied at once;
// an invalid value is highlighted at its field and is not saved.
public partial class SettingsWindow : Window
{
    private const int CustomInterval = -1;
    private const int RandomInterval = -2;
    private static readonly int[] PresetSeconds = [30, 60, 300, 600, 900, 1800];
    private static readonly string[] Palette =
        ["#FFFFFF", "#F2F2F2", "#BDBDBD", "#1E1E1E", "#000000", "#202020", "#FFD54F", "#81C784", "#64B5F6", "#E57373", "#BA68C8", "#F5F5F5"];
    private static readonly (string Name, string Action)[] Hotkeys =
    [
        ("Ctrl+Alt+→", "следующая фраза"),
        ("Ctrl+Alt+←", "предыдущая фраза"),
        ("Ctrl+Alt+Пробел", "показать перевод"),
        ("Ctrl+Alt+P", "пауза / продолжить"),
        ("Ctrl+Alt+H", "скрыть / показать"),
    ];
    private const string IdleStatus = "Изменения сохраняются сразу.";

    private readonly Dictionary<Control, string> _errors = new();
    private bool _loading;
    private bool _applying;
    private int _previousInterval;

    public SettingsWindow(AppController controller)
    {
        InitializeComponent();
        Controller = controller;

        FillCombos();
        WireFields();
        LoadValues();

        Controller.StateChanged += OnStateChanged;
        Closed += (_, _) => Controller.StateChanged -= OnStateChanged;
        Activated += (_, _) => LoadPlacement();   // the widget may have been dragged meanwhile
    }

    public AppController Controller { get; }

    // Always the current object: a restore from a backup replaces it.
    private AppSettings S => Controller.Settings;

    // ---- setup ----

    private static void Fill(ComboBox box, params (string Text, object Value)[] items)
    {
        box.ItemsSource = items.Select(i => new Option(i.Text, i.Value)).ToList();
        box.DisplayMemberPath = nameof(Option.Text);
        box.SelectedValuePath = nameof(Option.Value);
    }

    private void FillCombos()
    {
        Fill(ModeBox, ("Виджет", DisplayMode.Widget), ("Карточка", DisplayMode.Popup));
        Fill(IntervalBox,
            ("30 секунд", 30), ("1 минута", 60), ("5 минут", 300), ("10 минут", 600), ("15 минут", 900), ("30 минут", 1800),
            ("Свой интервал", CustomInterval), ("Случайный в диапазоне", RandomInterval));
        Fill(UnitBox, ("секунды", 1), ("минуты", 60));
        var translation = new (string, object)[]
        {
            ("Сразу", TranslationMode.Immediate), ("По клику", TranslationMode.OnClick),
            ("Через N секунд", TranslationMode.Delayed), ("Никогда", TranslationMode.Never),
        };
        Fill(WidgetTranslationBox, translation);
        Fill(PopupTranslationBox, translation);
        Fill(ThemeBox, ("Светлая", "Light"), ("Тёмная", "Dark"), ("Своя", "Custom"));
        Fill(CornerBox,
            ("Слева сверху", PopupCorner.TopLeft), ("Справа сверху", PopupCorner.TopRight),
            ("Слева снизу", PopupCorner.BottomLeft), ("Справа снизу", PopupCorner.BottomRight));
        Fill(MonitorBox, ("Где сейчас курсор", PopupMonitor.Cursor), ("Основной монитор", PopupMonitor.Primary));

        FontBox.ItemsSource = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().Order().ToList();

        BuildPalette(TextPalette, TextColorBox);
        BuildPalette(BackPalette, BackColorBox);
        var screens = System.Windows.Forms.Screen.AllScreens;
        MonitorInfo.Text = $"подключено мониторов: {screens.Length}";
        DataDirText.Text = AppPaths.DataDir;
    }

    private static void BuildPalette(WrapPanel panel, TextBox target)
    {
        foreach (var hex in Palette)
        {
            var button = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 4, 4), ToolTip = hex,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            button.Click += (_, _) => target.Text = hex;
            panel.Children.Add(button);
        }
    }

    private void WireFields()
    {
        Combo<DisplayMode>(ModeBox, v => S.Mode = v);
        IntervalBox.SelectionChanged += (_, _) => OnIntervalChanged();
        UnitBox.SelectionChanged += (_, _) => OnIntervalChanged();
        CustomBox.TextChanged += (_, _) => OnIntervalChanged();
        RandomMinBox.TextChanged += (_, _) => OnIntervalChanged();
        RandomMaxBox.TextChanged += (_, _) => OnIntervalChanged();
        IntField(PopupDurationBox, 3, 120, v => S.PopupDurationSeconds = v);
        Combo<TranslationMode>(WidgetTranslationBox, v => S.WidgetTranslation = v);
        Combo<TranslationMode>(PopupTranslationBox, v => S.PopupTranslation = v);
        IntField(DelayBox, 1, 60, v => S.TranslationDelaySeconds = v);

        Check(QuizEnabledBox, v => S.QuizEnabled = v);
        IntField(QuizPercentBox, 0, 100, v => S.QuizPercent = v);

        ThemeBox.SelectionChanged += (_, _) => OnThemeChanged();
        FontBox.SelectionChanged += (_, _) =>
        {
            if (!_loading && FontBox.SelectedItem is string font)
            {
                S.Appearance.FontFamily = font;
                Apply();
            }
        };
        IntField(FontSizeBox, 10, 48, v => S.Appearance.FontSize = v);
        ColorField(TextColorBox, TextSwatch, hex => S.Appearance.TextColor = hex);
        ColorField(BackColorBox, BackSwatch, hex => S.Appearance.BackgroundColor = hex);
        IntField(OpacityBox, 20, 100, v => S.Appearance.Opacity = v);
        Check(TopMostBox, v => S.Appearance.TopMost = v);
        Check(ControlsOnHoverBox, v => S.Appearance.ControlsOnHover = v);
        Check(LockPositionBox, v => S.Appearance.LockPosition = v);
        IntField(WidgetWidthBox, 160, 1200, v => S.Widget.Width = v);
        IntField(WidgetHeightBox, 40, 600, v => S.Widget.Height = v);
        WidgetXBox.TextChanged += (_, _) => OnWidgetPositionChanged();
        WidgetYBox.TextChanged += (_, _) => OnWidgetPositionChanged();
        ResetCornerButton.Click += (_, _) =>
        {
            S.Widget.X = null;
            S.Widget.Y = null;
            Apply();
            LoadPlacement();
        };
        IntField(PopupWidthBox, 160, 1200, v => S.Popup.Width = v);
        IntField(PopupHeightBox, 40, 600, v => S.Popup.Height = v);
        Combo<PopupCorner>(CornerBox, v => S.Popup.Corner = v);
        Combo<PopupMonitor>(MonitorBox, v => S.Popup.Monitor = v);

        Check(FullscreenBox, v => S.HideOnFullscreen = v);
        Check(QuietEnabledBox, v => S.QuietHours.Enabled = v);
        TimeField(QuietFromBox, v => S.QuietHours.From = v);
        TimeField(QuietToBox, v => S.QuietHours.To = v);
        Check(IdleEnabledBox, v => S.PauseWhenIdle = v);
        IntField(IdleMinutesBox, 1, 240, v => S.IdleMinutes = v);
        Check(HotkeysBox, v => S.HotkeysEnabled = v);
        Check(AutostartBox, v => S.StartWithWindows = v);

        BackupButton.Click += (_, _) => OnBackupClicked();
        RestoreButton.Click += (_, _) => OnRestoreClicked();
        OpenFolderButton.Click += (_, _) => OpenDataFolder();
    }

    // ---- field helpers: change -> validate -> store -> ApplySettings ----

    private void Apply()
    {
        _applying = true;
        try
        {
            Controller.ApplySettings();
        }
        finally
        {
            _applying = false;
        }
        RefreshHotkeys();
        UpdateEnabled();
    }

    private void SetError(Control field, string? message)
    {
        if (message is null)
        {
            _errors.Remove(field);
            field.ClearValue(Control.BorderBrushProperty);
            field.ClearValue(FrameworkElement.ToolTipProperty);
        }
        else
        {
            _errors[field] = message;
            field.BorderBrush = Brushes.Red;
            field.ToolTip = message;
        }
        if (_errors.Count == 0)
        {
            StatusText.Text = IdleStatus;
            StatusText.Foreground = Brushes.Gray;
        }
        else
        {
            StatusText.Text = "Не сохранено: " + _errors.Values.First();
            StatusText.Foreground = Brushes.Firebrick;
        }
    }

    private void IntField(TextBox box, int min, int max, Action<int> store) =>
        box.TextChanged += (_, _) =>
        {
            if (_loading)
                return;
            var error = SettingsInput.TryInt(box.Text, min, max, out var value);
            SetError(box, error);
            if (error is null)
            {
                store(value);
                Apply();
            }
        };

    private void TimeField(TextBox box, Action<TimeOnly> store) =>
        box.TextChanged += (_, _) =>
        {
            if (_loading)
                return;
            var error = SettingsInput.TryTime(box.Text, out var value);
            SetError(box, error);
            if (error is null)
            {
                store(value);
                Apply();
            }
        };

    private void ColorField(TextBox box, Border swatch, Action<string> store) =>
        box.TextChanged += (_, _) =>
        {
            var error = SettingsInput.TryHex(box.Text, out var hex);
            if (error is null)
                swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            if (_loading)
                return;
            SetError(box, error);
            if (error is null)
            {
                store(hex);
                S.Appearance.Theme = "Custom";   // a hand-picked color leaves the ready-made themes
                Quiet(() => ThemeBox.SelectedValue = "Custom");
                Apply();
            }
        };

    private void Check(CheckBox box, Action<bool> store)
    {
        void OnChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading)
                return;
            store(box.IsChecked == true);
            Apply();
        }
        box.Checked += OnChanged;
        box.Unchecked += OnChanged;
    }

    private void Combo<T>(ComboBox box, Action<T> store) =>
        box.SelectionChanged += (_, _) =>
        {
            if (_loading || box.SelectedValue is not T value)
                return;
            store(value);
            Apply();
        };

    // Runs `change` without the field handlers reacting.
    private void Quiet(Action change)
    {
        var before = _loading;
        _loading = true;
        try
        {
            change();
        }
        finally
        {
            _loading = before;
        }
    }

    // ---- interval, theme, position (fields that depend on each other) ----

    private int UnitFactor => UnitBox.SelectedValue is int f ? f : 1;

    private void OnIntervalChanged()
    {
        if (_loading || IntervalBox.SelectedValue is not int kind)
            return;
        if (kind != _previousInterval)
        {
            // Start the new editor from the value in force, so it is valid right away.
            Quiet(() => ShowIntervalValues(kind));
            _previousInterval = kind;
        }
        UpdateIntervalPanels(kind);

        if (kind > 0)
        {
            SetError(CustomBox, null);
            SetError(RandomMinBox, null);
            SetError(RandomMaxBox, null);
            S.Interval.Kind = IntervalKind.Fixed;
            S.Interval.Seconds = kind;
            Apply();
        }
        else if (kind == CustomInterval)
        {
            var error = SettingsInput.TryIntervalSeconds(CustomBox.Text, UnitFactor, out var seconds);
            SetError(CustomBox, error);
            if (error is null)
            {
                S.Interval.Kind = IntervalKind.Fixed;
                S.Interval.Seconds = seconds;
                Apply();
            }
        }
        else
        {
            var minError = SettingsInput.TryIntervalSeconds(RandomMinBox.Text, UnitFactor, out var min);
            var maxError = SettingsInput.TryIntervalSeconds(RandomMaxBox.Text, UnitFactor, out var max);
            if (minError is null && maxError is null && min > max)
            {
                const string order = "«от» не может быть больше «до»";
                SetError(RandomMinBox, order);
                SetError(RandomMaxBox, order);
                return;
            }
            SetError(RandomMinBox, minError);
            SetError(RandomMaxBox, maxError);
            if (minError is null && maxError is null)
            {
                S.Interval.Kind = IntervalKind.Random;
                S.Interval.MinSeconds = min;
                S.Interval.MaxSeconds = max;
                Apply();
            }
        }
    }

    private void ShowIntervalValues(int kind)
    {
        var interval = S.Interval;
        int[] values = kind == RandomInterval ? [interval.MinSeconds, interval.MaxSeconds] : [interval.Seconds];
        var unit = values.All(v => v % 60 == 0) ? 60 : 1;
        UnitBox.SelectedValue = unit;
        CustomBox.Text = (interval.Seconds / unit).ToString();
        RandomMinBox.Text = (interval.MinSeconds / unit).ToString();
        RandomMaxBox.Text = (interval.MaxSeconds / unit).ToString();
    }

    private void UpdateIntervalPanels(int kind)
    {
        CustomPanel.Visibility = kind == CustomInterval ? Visibility.Visible : Visibility.Collapsed;
        RandomPanel.Visibility = kind == RandomInterval ? Visibility.Visible : Visibility.Collapsed;
        UnitPanel.Visibility = kind < 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnThemeChanged()
    {
        if (_loading || ThemeBox.SelectedValue is not string theme)
            return;
        S.Appearance.Theme = theme;
        if (theme is "Light" or "Dark")
        {
            var (text, back) = theme == "Light" ? ("#1E1E1E", "#F5F5F5") : ("#F2F2F2", "#202020");
            S.Appearance.TextColor = text;
            S.Appearance.BackgroundColor = back;
            Quiet(() =>
            {
                TextColorBox.Text = text;
                BackColorBox.Text = back;
            });
            SetError(TextColorBox, null);
            SetError(BackColorBox, null);
        }
        Apply();
    }

    private void OnWidgetPositionChanged()
    {
        if (_loading)
            return;
        var xError = SettingsInput.TryCoordinate(WidgetXBox.Text, out var x);
        var yError = SettingsInput.TryCoordinate(WidgetYBox.Text, out var y);
        if (xError is null && yError is null && x.HasValue != y.HasValue)
        {
            const string both = "Задайте X и Y вместе (или оставьте оба пустыми)";
            if (x.HasValue)
                yError = both;
            else
                xError = both;
        }
        SetError(WidgetXBox, xError);
        SetError(WidgetYBox, yError);
        if (xError is null && yError is null)
        {
            S.Widget.X = x;
            S.Widget.Y = y;
            Apply();
        }
    }

    // ---- loading values into the controls ----

    private void LoadValues()
    {
        Quiet(() =>
        {
            foreach (var field in _errors.Keys.ToList())
                SetError(field, null);

            ModeBox.SelectedValue = S.Mode;
            var interval = S.Interval;
            var kind = interval.Kind == IntervalKind.Random ? RandomInterval
                : PresetSeconds.Contains(interval.Seconds) ? interval.Seconds
                : CustomInterval;
            IntervalBox.SelectedValue = kind;
            _previousInterval = kind;
            ShowIntervalValues(kind == RandomInterval ? RandomInterval : CustomInterval);
            UpdateIntervalPanels(kind);
            PopupDurationBox.Text = S.PopupDurationSeconds.ToString();
            WidgetTranslationBox.SelectedValue = S.WidgetTranslation;
            PopupTranslationBox.SelectedValue = S.PopupTranslation;
            DelayBox.Text = S.TranslationDelaySeconds.ToString();

            QuizEnabledBox.IsChecked = S.QuizEnabled;
            QuizPercentBox.Text = S.QuizPercent.ToString();

            var look = S.Appearance;
            ThemeBox.SelectedValue = look.Theme is "Light" or "Dark" ? look.Theme : "Custom";
            if (!FontBox.Items.Contains(look.FontFamily))
                FontBox.ItemsSource = FontBox.Items.Cast<string>().Append(look.FontFamily).Order().ToList();
            FontBox.SelectedItem = look.FontFamily;
            FontSizeBox.Text = look.FontSize.ToString();
            TextColorBox.Text = look.TextColor;
            BackColorBox.Text = look.BackgroundColor;
            OpacityBox.Text = look.Opacity.ToString();
            TopMostBox.IsChecked = look.TopMost;
            ControlsOnHoverBox.IsChecked = look.ControlsOnHover;
            LockPositionBox.IsChecked = look.LockPosition;
            LoadPlacement();
            PopupWidthBox.Text = S.Popup.Width.ToString("0");
            PopupHeightBox.Text = S.Popup.Height.ToString("0");
            CornerBox.SelectedValue = S.Popup.Corner;
            MonitorBox.SelectedValue = S.Popup.Monitor;

            FullscreenBox.IsChecked = S.HideOnFullscreen;
            QuietEnabledBox.IsChecked = S.QuietHours.Enabled;
            QuietFromBox.Text = S.QuietHours.From.ToString("HH:mm");
            QuietToBox.Text = S.QuietHours.To.ToString("HH:mm");
            IdleEnabledBox.IsChecked = S.PauseWhenIdle;
            IdleMinutesBox.Text = S.IdleMinutes.ToString();
            HotkeysBox.IsChecked = S.HotkeysEnabled;
            AutostartBox.IsChecked = S.StartWithWindows;
        });
        RefreshHotkeys();
        UpdateEnabled();
    }

    // Size and position of the widget: they also change by dragging it.
    private void LoadPlacement()
    {
        Quiet(() =>
        {
            foreach (var box in new[] { WidgetWidthBox, WidgetHeightBox, WidgetXBox, WidgetYBox })
                SetError(box, null);
            WidgetWidthBox.Text = S.Widget.Width.ToString("0");
            WidgetHeightBox.Text = S.Widget.Height.ToString("0");
            WidgetXBox.Text = S.Widget.X is { } x ? x.ToString("0") : "";
            WidgetYBox.Text = S.Widget.Y is { } y ? y.ToString("0") : "";
        });
    }

    private void UpdateEnabled()
    {
        QuizPercentBox.IsEnabled = QuizEnabledBox.IsChecked == true;
        QuietFromBox.IsEnabled = QuietToBox.IsEnabled = QuietEnabledBox.IsChecked == true;
        IdleMinutesBox.IsEnabled = IdleEnabledBox.IsChecked == true;
    }

    // Shortcuts another program holds are marked "занято"; the rest keep working.
    private void RefreshHotkeys()
    {
        HotkeyList.Items.Clear();
        var enabled = S.HotkeysEnabled;
        var failed = enabled ? Controller.FailedHotkeys : [];
        foreach (var (name, action) in Hotkeys)
        {
            var line = new TextBlock { Text = $"{name} — {action}", Margin = new Thickness(0, 0, 0, 3) };
            if (!enabled)
                line.Foreground = Brushes.Gray;
            if (failed.Contains(name))
            {
                line.Inlines.Add(new System.Windows.Documents.Run("  занято другой программой") { Foreground = Brushes.Firebrick });
                line.ToolTip = "Это сочетание уже занято другой программой; остальные работают";
            }
            HotkeyList.Items.Add(line);
        }
    }

    // Tray or hotkeys may change these while the window is open.
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_applying)
            return;
        Quiet(() =>
        {
            ModeBox.SelectedValue = S.Mode;
            AutostartBox.IsChecked = S.StartWithWindows;
        });
        RefreshHotkeys();
    }

    // ---- data tab ----

    // Public so that the file work is one call; the buttons add only the dialogs.
    public void CreateBackupTo(string zipPath) => BackupService.CreateBackup(zipPath);

    // Returns null on success, otherwise the reason (the data stays as it was).
    public string? RestoreFrom(string zipPath)
    {
        try
        {
            Controller.RestoreFromBackup(zipPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Restore from backup failed", ex);
            return ex.Message;
        }
        LoadValues();
        return null;
    }

    private void OnBackupClicked()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Резервная копия",
            Filter = "Резервная копия (*.zip)|*.zip",
            FileName = $"WinDeutsch-{DateTime.Now:yyyyMMdd-HHmm}.zip",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            CreateBackupTo(dialog.FileName);
            DataResultText.Foreground = Brushes.DarkGreen;
            DataResultText.Text = $"Копия сохранена: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Log.Error("Backup failed", ex);
            DataResultText.Foreground = Brushes.Firebrick;
            DataResultText.Text = $"Не удалось создать копию: {ex.Message}";
        }
    }

    private void OnRestoreClicked()
    {
        var dialog = new OpenFileDialog { Title = "Восстановить из копии", Filter = "Резервная копия (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true)
            return;
        var confirm = MessageBox.Show(this,
            "Текущие фразы и настройки будут заменены данными из копии. Перед этим текущее состояние автоматически сохранится в папке backups. Продолжить?",
            "Восстановить из копии", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return;

        var error = RestoreFrom(dialog.FileName);
        if (error is null)
        {
            DataResultText.Foreground = Brushes.DarkGreen;
            DataResultText.Text = "Данные восстановлены из копии.";
            return;
        }
        DataResultText.Foreground = Brushes.Firebrick;
        DataResultText.Text = $"Не удалось восстановить: {error}";
        MessageBox.Show(this, $"Этот файл не подошёл, данные не изменены.\n{error}", "Восстановить из копии",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the data folder", ex);
            DataResultText.Foreground = Brushes.Firebrick;
            DataResultText.Text = $"Не удалось открыть папку: {ex.Message}";
        }
    }
}
