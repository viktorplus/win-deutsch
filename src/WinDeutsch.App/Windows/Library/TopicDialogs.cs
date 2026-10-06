using System.Windows;
using System.Windows.Controls;
using WinDeutsch.Core.Models;

namespace WinDeutsch.App.Windows.Library;

// Small code-built dialogs: topic name/category and "pick a topic".
internal static class TopicDialogs
{
    // Returns (name, category) or null when cancelled. Category may be empty -> null.
    public static (string Name, string? Category)? EditTopic(
        Window owner, string title, string name, string? category, IEnumerable<string> categories)
    {
        var nameBox = new TextBox { Text = name, Margin = new Thickness(0, 2, 0, 10) };
        var categoryBox = new ComboBox
        {
            IsEditable = true,
            Text = category ?? "",
            ItemsSource = categories.ToList(),
            Margin = new Thickness(0, 2, 0, 10),
        };
        var ok = new Button { Content = "Сохранить", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Width = 90 };
        var window = BuildWindow(owner, title, 380,
            [Label("Название темы"), nameBox, Label("Категория (необязательно)"), categoryBox], ok, cancel);
        ok.IsEnabled = !string.IsNullOrWhiteSpace(name);
        nameBox.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
        ok.Click += (_, _) => window.DialogResult = true;
        nameBox.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        if (window.ShowDialog() != true)
            return null;
        var cat = categoryBox.Text.Trim();
        return (nameBox.Text.Trim(), cat.Length == 0 ? null : cat);
    }

    // Returns the chosen topic or null when cancelled.
    public static Topic? PickTopic(Window owner, string title, IReadOnlyList<Topic> topics)
    {
        var box = new ComboBox
        {
            ItemsSource = topics,
            DisplayMemberPath = nameof(Topic.Name),
            SelectedIndex = topics.Count > 0 ? 0 : -1,
            Margin = new Thickness(0, 2, 0, 10),
        };
        var ok = new Button { Content = "Перенести", IsDefault = true, Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Width = 90 };
        var window = BuildWindow(owner, title, 380, [Label("Тема"), box], ok, cancel);
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? box.SelectedItem as Topic : null;
    }

    private static TextBlock Label(string text) => new() { Text = text };

    private static Window BuildWindow(Window owner, string title, double width, UIElement[] content, Button ok, Button cancel)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        foreach (var element in content)
            panel.Children.Add(element);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel },
        });
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = owner,
            Content = panel,
        };
    }
}
