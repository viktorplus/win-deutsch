using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinDeutsch.App.Windows.Library;
using WinDeutsch.Core;
using WinDeutsch.Core.Import;
using WinDeutsch.Core.Learning;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Seed;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.App.Windows;

// Library: topic tree + phrase list, statistics, history. The repository is always taken from
// Controller (it is replaced after a restore from backup).
public partial class LibraryWindow : Window
{
    private const string UncategorizedName = "Без категории";
    private const int HistoryLimit = 500;

    private readonly TimeProvider _time = TimeProvider.System;
    private List<Phrase> _phrases = [];
    private IReadOnlyList<Topic> _topics = [];
    private TopicNode _root = new("all", "Все темы");
    private TopicNode _selectedNode;
    private bool _loading = true;   // true until the constructor finished (XAML fires events during InitializeComponent)

    public LibraryWindow(AppController controller)
    {
        InitializeComponent();
        Controller = controller;
        _selectedNode = _root;
        _loading = false;
        ReloadAll();
        Activated += (_, _) => OnActivatedRefresh();
    }

    public AppController Controller { get; }

    // The phrase the library should select.
    public long? SelectedPhraseId { get; private set; }

    private PhraseRepository Repo => Controller.Repository;

    // Selects the phrase in the list (resets search, filter and tree scope so it is visible).
    public void SelectPhrase(long id)
    {
        SelectedPhraseId = id;
        Tabs.SelectedIndex = 0;
        ReloadAll();
        _loading = true;
        SearchBox.Text = "";
        FilterBox.SelectedIndex = 0;
        _selectedNode.IsSelected = false;
        _selectedNode = _root;
        _root.IsSelected = true;
        _loading = false;
        ApplyFilter();
        var row = PhraseGrid.Items.OfType<PhraseRow>().FirstOrDefault(r => r.Id == id);
        if (row is null)
            return;
        PhraseGrid.SelectedItems.Clear();
        PhraseGrid.SelectedItem = row;
        PhraseGrid.ScrollIntoView(row);
    }

    // ---- loading ----

    private void ReloadAll()
    {
        _topics = Repo.GetTopics();
        _phrases = Repo.GetPhrases().ToList();
        BuildTree();
        ApplyFilter();
        RefreshSecondaryTab();
    }

    private void OnActivatedRefresh()
    {
        // Refresh only when something changed (e.g. from the card), so a click that activates the window is not eaten.
        if (_loading)
            return;
        var phrases = Repo.GetPhrases().ToList();
        if (phrases.SequenceEqual(_phrases))
            return;
        // The card changes only learning state: keep the tree, so a click on a topic's check box survives.
        if (Repo.GetTopics().SequenceEqual(_topics)
            && phrases.Select(p => (p.Id, p.TopicId)).SequenceEqual(_phrases.Select(p => (p.Id, p.TopicId))))
        {
            _phrases = phrases;
            ApplyFilter();
            RefreshSecondaryTab();
            return;
        }
        ReloadAll();
    }

    private void BuildTree()
    {
        var previousKey = _selectedNode.Key;
        var counts = _phrases.GroupBy(p => p.TopicId).ToDictionary(g => g.Key, g => g.Count());
        var root = new TopicNode("all", "Все темы") { Count = _phrases.Count };
        var categories = new List<(string? Name, TopicNode Node)>();

        foreach (var topic in _topics)
        {
            var name = string.IsNullOrWhiteSpace(topic.Category) ? null : topic.Category.Trim();
            var index = categories.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                categories.Add((name, new TopicNode("cat:" + (name ?? ""), name ?? UncategorizedName)));
                index = categories.Count - 1;
            }
            var count = counts.GetValueOrDefault(topic.Id);
            var leaf = new TopicNode("topic:" + topic.Id, topic.Name, topic) { Count = count };
            leaf.SetEnabled(topic.IsEnabled);
            categories[index].Node.Children.Add(leaf);
            categories[index].Node.Count += count;
        }

        // Uncategorized topics go last.
        foreach (var (_, node) in categories.OrderBy(c => c.Name is null ? 1 : 0))
            root.Children.Add(node);

        foreach (var node in Flatten(root))
            node.Toggled = OnNodeToggled;
        root.RecomputeState();

        _root = root;
        _selectedNode = Flatten(root).FirstOrDefault(n => n.Key == previousKey) ?? root;
        _loading = true;
        _selectedNode.IsSelected = true;
        TopicTree.ItemsSource = new[] { root };
        _loading = false;
    }

    private static IEnumerable<TopicNode> Flatten(TopicNode n) =>
        new[] { n }.Concat(n.Children.SelectMany(Flatten));

    private HashSet<long>? ScopeTopicIds() =>
        _selectedNode == _root ? null : _selectedNode.Topics().Select(n => n.Topic!.Id).ToHashSet();

    private void ApplyFilter()
    {
        if (_loading)
            return;
        var kept = SelectedIds().ToHashSet();
        var scope = ScopeTopicIds();
        var query = SearchBox.Text.Trim();
        var now = _time.GetUtcNow();
        var filter = FilterBox.SelectedIndex;
        var names = _topics.ToDictionary(t => t.Id, t => t.Name);

        var rows = new List<PhraseRow>();
        var inScope = 0;
        foreach (var p in _phrases)
        {
            if (scope is not null && !scope.Contains(p.TopicId))
                continue;
            inScope++;
            if (query.Length > 0
                && p.German.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                && p.Russian.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var snoozed = p.Status == PhraseStatus.Active && p.SnoozedUntil is { } until && until > now;
            var match = filter switch
            {
                1 => p.Status == PhraseStatus.Active && !snoozed,
                2 => p.Status == PhraseStatus.Excluded,
                3 => snoozed,
                >= 4 => LearningRules.GetFamiliarity(p) == (Familiarity)(filter - 4),
                _ => true,
            };
            if (match)
                rows.Add(new PhraseRow(p, names.GetValueOrDefault(p.TopicId, ""), now));
        }

        PhraseGrid.ItemsSource = rows;
        foreach (var row in rows.Where(r => kept.Contains(r.Id)))
            PhraseGrid.SelectedItems.Add(row);
        CountText.Text = rows.Count == 0
            ? "Ничего не найдено"
            : $"Показано {rows.Count} из {inScope}";
    }

    // ---- selection helpers ----

    private IEnumerable<long> SelectedIds() =>
        PhraseGrid.SelectedItems.OfType<PhraseRow>().Select(r => r.Id);

    private List<long> RequireSelection()
    {
        var ids = SelectedIds().ToList();
        if (ids.Count == 0)
            MessageBox.Show(this, "Выделите одну или несколько фраз.", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);
        return ids;
    }

    private long? SelectedTopicId() => _selectedNode.Topic?.Id;

    // Runs a repository change, then refreshes this window and the running card.
    private void Mutate(Action<PhraseRepository> change)
    {
        try
        {
            change(Repo);
        }
        catch (Exception ex)
        {
            Log.Error("Library: change failed", ex);
            MessageBox.Show(this, $"Не удалось сохранить изменения: {ex.Message}", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ReloadAll();
        Controller.ReloadData();
    }

    // ---- tree ----

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TopicNode node)
            _selectedNode = node;
        ApplyFilter();
    }

    private void OnNodeToggled(TopicNode node, bool enabled)
    {
        // A topic's check box changes only after its write succeeded, so the tree never disagrees with the database.
        try
        {
            foreach (var leaf in node.Topics())
            {
                Repo.SetTopicEnabled(leaf.Topic!.Id, enabled);
                leaf.SetEnabled(enabled);
            }
            _topics = Repo.GetTopics();   // keeps the activation refresh from rebuilding the tree for this change
        }
        catch (Exception ex)
        {
            Log.Error("Library: toggle failed", ex);
            // Not from inside the binding update (a modal loop there is not allowed); after it finished.
            Dispatcher.BeginInvoke(() => MessageBox.Show(this, $"Не удалось сохранить изменения: {ex.Message}", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        _root.RecomputeState();
        Controller.ReloadData();
    }

    private IEnumerable<string> CategoryNames() =>
        _topics.Select(t => t.Category?.Trim()).OfType<string>().Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    private void OnAddTopic(object sender, RoutedEventArgs e)
    {
        var defaultCategory = _selectedNode.Topic?.Category
            ?? (_selectedNode.Key.StartsWith("cat:") && _selectedNode.Key.Length > 4 ? _selectedNode.Name : null);
        if (TopicDialogs.EditTopic(this, "Новая тема", "", defaultCategory, CategoryNames()) is not var (name, category))
            return;
        Mutate(r => r.AddTopic(name, category));
    }

    private void OnRenameTopic(object sender, RoutedEventArgs e)
    {
        if (_selectedNode.Topic is not { } topic)
        {
            MessageBox.Show(this, "Выберите тему в дереве.", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (TopicDialogs.EditTopic(this, "Изменить тему", topic.Name, topic.Category, CategoryNames()) is not var (name, category))
            return;
        Mutate(r => r.RenameTopic(topic.Id, name, category));
    }

    private void OnDeleteTopic(object sender, RoutedEventArgs e)
    {
        if (_selectedNode.Topic is not { } topic)
        {
            MessageBox.Show(this, "Выберите тему в дереве.", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var count = _phrases.Count(p => p.TopicId == topic.Id);
        var text = count == 0
            ? $"Удалить тему «{topic.Name}»?"
            : $"Удалить тему «{topic.Name}»? Вместе с ней будет удалено фраз: {count}.";
        if (MessageBox.Show(this, text, "Удаление темы", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        Mutate(r => r.DeleteTopic(topic.Id));
    }

    private void OnDeleteCategory(object sender, RoutedEventArgs e)
    {
        // "cat:" alone is the "uncategorized" group, not a category.
        if (!_selectedNode.Key.StartsWith("cat:") || _selectedNode.Key.Length == 4)
        {
            MessageBox.Show(this, "Выберите категорию в дереве.", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ids = _selectedNode.Topics().Select(n => n.Topic!.Id).ToList();
        var text = $"Удалить категорию «{_selectedNode.Name}»? Вместе с ней будут удалены тем: {ids.Count}, " +
                   $"фраз: {_selectedNode.Count} и история их показов.";
        if (MessageBox.Show(this, text, "Удаление категории", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        Mutate(r => r.DeleteTopics(ids));
    }

    // ---- phrases ----

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PhraseGrid.SelectedItem is PhraseRow && IsOverRow(e.OriginalSource))
            OnEditPhrase(sender, e);
    }

    private static bool IsOverRow(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is DataGridRow)
                return true;
            if (d is System.Windows.Controls.Primitives.DataGridColumnHeader)
                return false;
        }
        return false;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            OnDeletePhrases(sender, e);
            e.Handled = true;
        }
    }

    private void OnAddPhrase(object sender, RoutedEventArgs e)
    {
        if (_topics.Count == 0)
        {
            MessageBox.Show(this, "Сначала создайте тему.", "Библиотека", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new PhraseEditDialog(_topics, SelectedTopicId() ?? 0) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        Mutate(r => r.AddPhrase(dialog.TopicId, dialog.German, dialog.Russian));
    }

    private void OnEditPhrase(object sender, RoutedEventArgs e)
    {
        if (PhraseGrid.SelectedItem is not PhraseRow row)
        {
            RequireSelection();
            return;
        }
        var phrase = row.Phrase;
        var dialog = new PhraseEditDialog(_topics, phrase.TopicId, phrase) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        Mutate(r =>
        {
            r.UpdatePhraseText(phrase.Id, dialog.German, dialog.Russian);
            if (dialog.TopicId != phrase.TopicId)
                r.MovePhrases([phrase.Id], dialog.TopicId);
        });
    }

    private void OnShowActions(object sender, RoutedEventArgs e) => OpenMenu("ActionsMenu", (Button)sender);

    private void OnShowExport(object sender, RoutedEventArgs e) => OpenMenu("ExportMenu", (Button)sender);

    private void OpenMenu(string key, Button anchor)
    {
        var menu = (ContextMenu)FindResource(key);
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnExclude(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is { Count: > 0 } ids)
            Mutate(r => r.SetStatus(ids, PhraseStatus.Excluded));
    }

    private void OnInclude(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is { Count: > 0 } ids)
            Mutate(r => r.SetStatus(ids, PhraseStatus.Active));
    }

    // Manual state: Unknown = level 0 for shown phrases (level 1 for never shown, so it is not "New"), Learning 3, Known 5.
    private void OnSetState(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is not { Count: > 0 } ids)
            return;
        var tag = (string)((MenuItem)sender).Tag;
        Mutate(r =>
        {
            if (tag == "unknown")
            {
                var shown = ids.Where(id => r.GetPhrase(id) is { ShowCount: > 0 }).ToList();
                var fresh = ids.Except(shown).ToList();
                if (shown.Count > 0)
                    r.SetLevel(shown, 0);
                if (fresh.Count > 0)
                    r.SetLevel(fresh, 1);
            }
            else
            {
                r.SetLevel(ids, tag == "learning" ? 3 : LearningRules.MaxLevel);
            }
        });
    }

    private void OnRepeatMore(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is not { Count: > 0 } ids)
            return;
        Mutate(r =>
        {
            var now = _time.GetUtcNow();
            foreach (var id in ids)
                if (r.GetPhrase(id) is { } phrase)
                    r.SaveLearningState(LearningRules.Apply(phrase, ShowOutcome.RepeatMore, now));
        });
    }

    private void OnSnooze(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is not { Count: > 0 } ids)
            return;
        var span = TimeSpan.Parse((string)((MenuItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        Mutate(r => r.Snooze(ids, _time.GetUtcNow() + span));
    }

    private void OnMove(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is not { Count: > 0 } ids)
            return;
        if (TopicDialogs.PickTopic(this, "Перенести в тему", _topics) is not { } topic)
            return;
        Mutate(r => r.MovePhrases(ids, topic.Id));
    }

    private void OnDeletePhrases(object sender, RoutedEventArgs e)
    {
        if (RequireSelection() is not { Count: > 0 } ids)
            return;
        var text = ids.Count == 1 ? "Удалить выбранную фразу?" : $"Удалить выбранные фразы ({ids.Count})?";
        if (MessageBox.Show(this, text + " Вместе с ними удалится история показов.", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        Mutate(r => r.DeletePhrases(ids));
    }

    // ---- import / export / seed ----

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportDialog(Controller, SelectedTopicId()) { Owner = this };
        dialog.ShowDialog();
        ReloadAll();
    }

    private void OnImportDictionaries(object sender, RoutedEventArgs e)
    {
        var folder = DictionaryFolder.Default;
        var files = DictionaryFolder.List(folder);
        if (files.Count == 0)
        {
            MessageBox.Show(this, $"В папке «{folder}» нет словарей (*.tsv).", "Словари", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var existing = _topics.Select(t => t.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = TopicDialogs.PickDictionaries(this, folder, files, existing);
        if (chosen.Count == 0)
            return;
        var added = 0;
        Mutate(r =>
        {
            foreach (var file in chosen)
                added += DictionaryFolder.Import(r, file);
        });
        MessageBox.Show(this, $"Добавлено фраз: {added}", "Словари", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportSelected(object sender, RoutedEventArgs e) => Export(ScopeTopicIds());

    private void OnExportAll(object sender, RoutedEventArgs e) => Export(null);

    private void Export(IReadOnlyCollection<long>? topicIds)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт фраз",
            Filter = "Таблица с табуляцией (*.tsv)|*.tsv",
            FileName = "phrases.tsv",
            DefaultExt = ".tsv",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var count = ExportTo(dialog.FileName, topicIds);
            MessageBox.Show(this, $"Экспортировано фраз: {count}", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
            MessageBox.Show(this, $"Не удалось записать файл: {ex.Message}", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Writes the phrases of the given topics (null = all) to a UTF-8 .tsv file; returns the phrase count.
    public int ExportTo(string path, IReadOnlyCollection<long>? topicIds)
    {
        var phrases = Repo.GetPhrases().Where(p => topicIds is null || topicIds.Contains(p.TopicId)).ToList();
        var topics = Repo.GetTopics().ToDictionary(t => t.Id);
        File.WriteAllText(path, Exporter.ToTsv(phrases, topics), new System.Text.UTF8Encoding(true));
        return phrases.Count;
    }

    private void OnRestoreSeed(object sender, RoutedEventArgs e)
    {
        try
        {
            var added = SeedData.RestoreMissing(Repo);
            ReloadAll();
            Controller.ReloadData();
            MessageBox.Show(this, added == 0 ? "Все базовые фразы на месте." : $"Возвращено фраз: {added}",
                "Базовый набор", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Restore seed failed", ex);
            MessageBox.Show(this, $"Не удалось восстановить набор: {ex.Message}", "Базовый набор", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- statistics & history ----

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && e.OriginalSource == Tabs)
            RefreshSecondaryTab();
    }

    private void RefreshSecondaryTab()
    {
        switch (Tabs.SelectedIndex)
        {
            case 1: LoadStatistics(); break;
            case 2: LoadHistory(); break;
        }
    }

    private void LoadStatistics()
    {
        var s = Repo.GetStatistics();
        static string Accuracy(int correct, int total) =>
            total == 0 ? "нет ответов" : $"{correct} из {total} ({Math.Round(100.0 * correct / total)}%)";
        StatsList.ItemsSource = new List<StatItem>
        {
            new("Всего фраз", s.TotalPhrases.ToString()),
            new("Новая", s.New.ToString()),
            new("Не знаю", s.Unknown.ToString()),
            new("Учу", s.Learning.ToString()),
            new("Знаю", s.Known.ToString()),
            new("Исключено", s.Excluded.ToString()),
            new("Показов сегодня", s.ShowsToday.ToString()),
            new("Показов за 7 дней", s.ShowsLast7Days.ToString()),
            new("Тестов сегодня", s.QuizzesToday.ToString()),
            new("Верных сегодня", Accuracy(s.CorrectToday, s.QuizzesToday)),
            new("Тестов всего", s.QuizzesTotal.ToString()),
            new("Верных всего", Accuracy(s.CorrectTotal, s.QuizzesTotal)),
        };
        HardestGrid.ItemsSource = s.Hardest.Select(h => new HardestRow(h.German, h.Russian, h.Wrong)).ToList();
        HardestEmpty.Visibility = s.Hardest.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HardestGrid.Visibility = s.Hardest.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LoadHistory()
    {
        var byId = _phrases.ToDictionary(p => p.Id);
        var rows = Repo.GetHistory(HistoryLimit).Select(h =>
        {
            var phrase = byId.GetValueOrDefault(h.Event.PhraseId);
            return new HistoryRow(
                LibraryText.Local(h.Event.At), h.Event.At, h.German,
                LibraryText.Mode(h.Event.Mode), LibraryText.Outcome(h.Event.Outcome),
                phrase?.ShowCount ?? 0,
                phrase?.LastShownAt is { } shown ? LibraryText.Local(shown) : "");
        }).ToList();
        HistoryGrid.ItemsSource = rows;
        HistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
