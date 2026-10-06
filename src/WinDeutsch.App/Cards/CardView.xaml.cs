using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;

namespace WinDeutsch.App.Cards;

// Shared card markup for the widget and the popup; every action goes to AppController.
public partial class CardView : UserControl
{
    private AppController? _controller;

    public CardView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
                oldVm.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is INotifyPropertyChanged newVm)
                newVm.PropertyChanged += OnViewModelChanged;
            UpdateControlsBar();
        };
        MouseEnter += (_, _) => UpdateControlsBar();
        MouseLeave += (_, _) => UpdateControlsBar();
    }

    // The widget lets the user drag the window by the card; the popup does not.
    public bool AllowDrag { get; set; }

    public void Attach(AppController controller)
    {
        _controller = controller;
        DataContext = controller.Card;
    }

    private CardViewModel? Vm => DataContext as CardViewModel;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => UpdateControlsBar();

    private void UpdateControlsBar()
    {
        var vm = Vm;
        var visible = vm is { ShowPhrase: true } && (!vm.ControlsOnHover || IsMouseOver);
        ControlsBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // A press on the card body: drag (widget) or click = reveal translation / open the library when empty.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled)
            return;
        e.Handled = true;

        var window = Window.GetWindow(this);
        if (AllowDrag && window is not null)
        {
            var before = new Point(window.Left, window.Top);
            window.DragMove();
            if (Math.Abs(window.Left - before.X) > 1 || Math.Abs(window.Top - before.Y) > 1)
                return;
        }
        if (_controller is null || Vm is not { } vm)
            return;
        if (vm.ShowEmpty)
            _controller.OpenLibrary();
        else if (vm.CanReveal)
            _controller.RevealTranslation();
    }

    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
            _controller?.AnswerQuiz(index);
    }

    private void OnDontKnowClick(object sender, RoutedEventArgs e) => _controller?.AnswerQuiz(-1);
    private void OnResumeClick(object sender, RoutedEventArgs e) => _controller?.Resume();
    private void OnPreviousClick(object sender, RoutedEventArgs e) => _controller?.Previous();
    private void OnNextClick(object sender, RoutedEventArgs e) => _controller?.Next();
    private void OnRevealClick(object sender, RoutedEventArgs e) => _controller?.RevealTranslation();
    private void OnKnownClick(object sender, RoutedEventArgs e) => _controller?.HandleOutcome(ShowOutcome.Known);
    private void OnUnknownClick(object sender, RoutedEventArgs e) => _controller?.HandleOutcome(ShowOutcome.Unknown);
    private void OnLaterClick(object sender, RoutedEventArgs e) => _controller?.HandleOutcome(ShowOutcome.Later);
    private void OnCloseClick(object sender, RoutedEventArgs e) => _controller?.CloseCard();

    private void OnPauseClick(object sender, RoutedEventArgs e) => OpenMenu((Button)sender,
        ("15 минут", () => _controller?.Pause(PauseOption.Minutes15)),
        ("1 час", () => _controller?.Pause(PauseOption.Hour1)),
        ("До завтра (06:00)", () => _controller?.Pause(PauseOption.UntilTomorrow)),
        ("Пока не сниму", () => _controller?.Pause(PauseOption.Indefinite)));

    private void OnMoreClick(object sender, RoutedEventArgs e) => OpenMenu((Button)sender,
        ("Исключить из обучения", () => _controller?.HandleOutcome(ShowOutcome.Excluded)),
        ("Повторить чаще", () => _controller?.HandleOutcome(ShowOutcome.RepeatMore)),
        ("Открыть в библиотеке", () => _controller?.OpenLibrary(_controller.CurrentPhraseId)));

    private static void OpenMenu(Button target, params (string Header, Action Action)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (var (header, action) in items)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
