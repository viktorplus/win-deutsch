using System.Windows;
using System.Windows.Media.Animation;
using WinDeutsch.App.Platform;
using WinDeutsch.Core.Settings;

namespace WinDeutsch.App.Windows;

// The card that pops up in a screen corner without taking focus, then fades out.
public partial class PopupWindow : Window
{
    private static readonly Duration FadeIn = TimeSpan.FromMilliseconds(180);
    private static readonly Duration FadeOut = TimeSpan.FromMilliseconds(300);

    private PopupPlacement _placement = new();
    private Rect _area;
    private bool _closing;

    public PopupWindow(AppController controller)
    {
        InitializeComponent();
        Card.Attach(controller);
        SourceInitialized += (_, _) => NoActivateWindow.Apply(this);
        SizeChanged += (_, _) => PlaceInCorner();
    }

    public bool IsShown => IsVisible && !_closing;

    // Shows (or keeps showing) the card on the configured monitor and corner, fading in when it was hidden.
    public void Pop(PopupPlacement placement)
    {
        _placement = placement;
        _area = placement.Monitor == PopupMonitor.Primary ? ScreenHelper.PrimaryWorkArea : ScreenHelper.CursorWorkArea;
        Width = Math.Clamp(placement.Width, 160, 1200);
        MinHeight = Math.Clamp(placement.Height, 40, 600);
        PlaceInCorner();

        if (IsShown)
            return;
        _closing = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeIn));
    }

    public void FadeAway()
    {
        if (!IsShown)
            return;
        _closing = true;
        var fade = new DoubleAnimation(Opacity, 0, FadeOut);
        fade.Completed += (_, _) =>
        {
            if (_closing)
                Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PlaceInCorner()
    {
        if (_area.IsEmpty)
            return;
        var right = _placement.Corner is PopupCorner.TopRight or PopupCorner.BottomRight;
        var bottom = _placement.Corner is PopupCorner.BottomLeft or PopupCorner.BottomRight;
        var height = ActualHeight > 0 ? ActualHeight : MinHeight;
        var p = ScreenHelper.CornerPosition(_area, new Size(Width, height), right, bottom);
        Left = p.X;
        Top = p.Y;
    }
}
