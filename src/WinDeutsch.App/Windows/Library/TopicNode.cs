using System.ComponentModel;
using System.Windows;
using WinDeutsch.Core.Models;

namespace WinDeutsch.App.Windows.Library;

// Node of the topic tree: root ("all topics") -> category -> topic. Check state is tri-state for groups.
internal sealed class TopicNode : INotifyPropertyChanged
{
    private bool? _isChecked = true;
    private bool _isSelected;

    public TopicNode(string key, string name, Topic? topic = null)
    {
        Key = key;
        Name = name;
        Topic = topic;
    }

    public string Key { get; }
    public string Name { get; }
    public Topic? Topic { get; }
    public int Count { get; set; }
    public List<TopicNode> Children { get; } = [];
    public bool IsExpanded { get; set; } = true;

    // Raised only for user clicks; the window writes the change to the database and sets the topics' states.
    public Action<TopicNode, bool>? Toggled { get; set; }

    public string Display => $"{Name} ({Count})";

    // "Indeterminate" is display-only (a partly enabled group); a click always enables or disables everything.
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            Toggled?.Invoke(this, value == true);
            // Push the actual state (it may have been rolled back) to the check box after the binding finished.
            Application.Current.Dispatcher.BeginInvoke(() => OnChanged(nameof(IsChecked)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnChanged(nameof(IsSelected));
        }
    }

    public IEnumerable<TopicNode> Topics() =>
        Topic is not null ? [this] : Children.SelectMany(c => c.Topics());

    // Recomputes group states bottom-up from the topics' own flags.
    public void RecomputeState()
    {
        foreach (var child in Children)
            child.RecomputeState();
        if (Topic is null && Children.Count > 0)
        {
            var on = Children.Count(c => c._isChecked == true);
            var off = Children.Count(c => c._isChecked == false);
            SetState(on == Children.Count ? true : off == Children.Count ? false : null);
        }
    }

    public void SetEnabled(bool enabled) => SetState(enabled);

    private void SetState(bool? state)
    {
        if (_isChecked == state)
            return;
        _isChecked = state;
        OnChanged(nameof(IsChecked));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
