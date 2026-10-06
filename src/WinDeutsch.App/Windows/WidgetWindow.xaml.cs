using System.Windows;
using System.Windows.Threading;
using WinDeutsch.App.Platform;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.App.Windows;

// The always-visible mini window. Position and size live in AppSettings.Widget (DIPs).
public partial class WidgetWindow : Window
{
    private readonly AppController _controller;
    private bool _applying;
    private double? _savedHeight;   // set while the window is temporarily taller for a quiz

    public WidgetWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Card.Attach(controller);
        SourceInitialized += (_, _) => NoActivateWindow.Apply(this);
        LocationChanged += (_, _) => StorePlacement();
        SizeChanged += (_, _) => StorePlacement();
        ScreenHelper.DisplaySettingsChanged += (_, _) => KeepOnScreen();
    }

    // Raised when the user moved or resized the window; the new values are already in Settings.Widget.
    public event EventHandler? PlacementChanged;

    public void ApplyLayout(AppSettings s)
    {
        _applying = true;
        try
        {
            Topmost = s.Appearance.TopMost;
            var locked = s.Appearance.LockPosition;
            ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            Card.AllowDrag = !locked;

            var size = new Size(Math.Clamp(s.Widget.Width, MinWidth, MaxWidth), Math.Clamp(s.Widget.Height, MinHeight, MaxHeight));
            var origin = s.Widget is { X: { } x, Y: { } y }
                ? new Point(x, y)
                : ScreenHelper.CornerPosition(ScreenHelper.PrimaryWorkArea, size, right: true, bottom: true);
            var rect = ScreenHelper.EnsureOnScreen(new Rect(origin, size));
            _savedHeight = null;
            Left = rect.Left;
            Top = rect.Top;
            Width = rect.Width;
            Height = rect.Height;
        }
        finally
        {
            _applying = false;
        }
        StorePlacement();
    }

    // A quiz needs more room than a phrase: grow upwards while it is shown, then return to the saved height.
    public void FitContent(bool quizShown)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _applying = true;
            try
            {
                if (quizShown)
                {
                    Card.Measure(new Size(ActualWidth, double.PositiveInfinity));
                    var needed = Math.Min(Card.DesiredSize.Height, MaxHeight);
                    if (needed <= ActualHeight)
                        return;
                    _savedHeight ??= Height;
                    Top -= needed - Height;
                    Height = needed;
                }
                else if (_savedHeight is { } saved)
                {
                    Top += Height - saved;
                    Height = saved;
                    _savedHeight = null;
                }
            }
            finally
            {
                _applying = false;
            }
        });
    }

    private void KeepOnScreen()
    {
        var rect = ScreenHelper.EnsureOnScreen(new Rect(Left, Top, Width, Height));
        if (rect.Left != Left || rect.Top != Top)
        {
            Left = rect.Left;
            Top = rect.Top;
        }
    }

    private void StorePlacement()
    {
        if (_applying || _savedHeight is not null)
            return;
        var w = _controller.Settings.Widget;
        if (w.X == Left && w.Y == Top && w.Width == Width && w.Height == Height)
            return;
        w.X = Left;
        w.Y = Top;
        w.Width = Width;
        w.Height = Height;
        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }
}
