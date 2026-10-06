using System.Windows;
using System.Windows.Controls;
using WinDeutsch.Core.Models;

namespace WinDeutsch.App.Windows;

// Add or edit one phrase. "Сохранить" stays disabled while the German text is empty.
public partial class PhraseEditDialog : Window
{
    public PhraseEditDialog(IReadOnlyList<Topic> topics, long selectedTopicId, Phrase? existing = null)
    {
        InitializeComponent();
        Title = existing is null ? "Новая фраза" : "Изменить фразу";
        TopicBox.ItemsSource = topics;
        TopicBox.SelectedItem = topics.FirstOrDefault(t => t.Id == selectedTopicId) ?? topics.FirstOrDefault();
        GermanBox.Text = existing?.German ?? "";
        RussianBox.Text = existing?.Russian ?? "";
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(GermanBox.Text);
        Loaded += (_, _) => GermanBox.Focus();
    }

    public long TopicId => (TopicBox.SelectedItem as Topic)?.Id ?? 0;
    public string German => GermanBox.Text.Trim();
    public string Russian => RussianBox.Text.Trim();

    private void OnGermanChanged(object sender, TextChangedEventArgs e) =>
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(GermanBox.Text) && TopicBox.SelectedItem is not null;

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
