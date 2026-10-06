using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinDeutsch.Core;
using WinDeutsch.Core.Import;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Storage;

namespace WinDeutsch.App.Windows;

// Import from a file or pasted text with a preview. PreviewImport/CommitImport hold all the logic;
// the handlers only show their results.
public partial class ImportDialog : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private ImportParseResult _parsed = new([], []);

    public ImportDialog(AppController controller, long? topicId = null)
    {
        InitializeComponent();
        _controller = controller;
        var topics = controller.Repository.GetTopics();
        TopicBox.ItemsSource = topics;
        TopicBox.SelectedItem = topics.FirstOrDefault(t => t.Id == topicId) ?? topics.FirstOrDefault();
        TopicBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnInputChanged));
        TopicBox.SelectionChanged += (_, _) => RestartPreview();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            PreviewImport();
        };
    }

    public int ImportedCount { get; private set; }

    public string SourceText
    {
        get => SourceBox.Text;
        set => SourceBox.Text = value;
    }

    // Existing topic name or a new one typed by the user.
    public string TopicName
    {
        get => TopicBox.Text.Trim();
        set => TopicBox.Text = value;
    }

    // Parses the text and counts what would be added; also refreshes the summary and the error list.
    public ImportPlan PreviewImport()
    {
        _debounce.Stop();
        _parsed = ImportParser.Parse(SourceBox.Text);
        var repo = _controller.Repository;
        var plan = new ImportService(repo).Preview(_parsed, FindTopic(repo)?.Id ?? 0);

        SummaryText.Text = _parsed.Rows.Count == 0 && _parsed.Errors.Count == 0
            ? "Нет строк для импорта"
            : $"Добавится {plan.ToAdd}, дубликатов {plan.Duplicates}, ошибок {plan.Errors.Count}";
        ErrorsList.ItemsSource = plan.Errors.Select(e => $"строка {e.LineNumber}: {e.Message}").ToList();
        var needsTopic = _parsed.Rows.Any(r => r.TopicName is null);
        ImportButton.IsEnabled = plan.ToAdd > 0 && (!needsTopic || TopicName.Length > 0);
        return plan;
    }

    // Adds the rows in one transaction (new topic created when needed) and refreshes the running app.
    public int CommitImport()
    {
        PreviewImport();
        var repo = _controller.Repository;
        var topic = FindTopic(repo);
        if (topic is null && TopicName.Length > 0)
            topic = repo.AddTopic(TopicName, null);
        var added = new ImportService(repo).Commit(_parsed, topic?.Id ?? 0);
        ImportedCount += added;
        _controller.ReloadData();
        return added;
    }

    private Topic? FindTopic(PhraseRepository repo) =>
        repo.GetTopics().FirstOrDefault(t => string.Equals(t.Name.Trim(), TopicName, StringComparison.OrdinalIgnoreCase));

    private void RestartPreview()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e) => RestartPreview();

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Файл с фразами",
            Filter = "Текстовые файлы (*.txt;*.csv;*.tsv)|*.txt;*.csv;*.tsv|Все файлы (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SourceBox.Text = TextFileReader.ReadAllTextDetect(dialog.FileName);
            PreviewImport();
        }
        catch (Exception ex)
        {
            Log.Error("Import: cannot read file", ex);
            MessageBox.Show(this, $"Не удалось прочитать файл: {ex.Message}", "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var added = CommitImport();
            MessageBox.Show(this, $"Добавлено фраз: {added}", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("Import failed", ex);
            MessageBox.Show(this, $"Импорт не удался: {ex.Message}", "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
