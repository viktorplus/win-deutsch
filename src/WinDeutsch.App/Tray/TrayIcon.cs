using WinDeutsch.App.Platform;
using WinDeutsch.Core.Models;
using WinDeutsch.Core.Scheduling;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WinDeutsch.App.Tray;

// Tray icon and its menu. The icon is drawn in code: no image files in the project.
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _controller;
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _activeIcon;
    private readonly Drawing.Icon _pausedIcon;

    public TrayIcon(AppController controller)
    {
        _controller = controller;
        _activeIcon = DrawIcon(Drawing.Color.FromArgb(0xD4, 0x9A, 0x00));
        _pausedIcon = DrawIcon(Drawing.Color.FromArgb(0x80, 0x80, 0x80));
        _icon = new Forms.NotifyIcon { Text = "Win Deutsch", Icon = _activeIcon, ContextMenuStrip = new Forms.ContextMenuStrip() };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu(_icon.ContextMenuStrip);
        _icon.MouseClick += OnMouseClick;
        BuildMenu(_icon.ContextMenuStrip);
        controller.StateChanged += (_, _) => Refresh();
        _icon.Visible = true;
        Refresh();
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(5000, "Win Deutsch", text, Forms.ToolTipIcon.Info);

    private void Refresh()
    {
        _icon.Icon = _controller.IsPaused ? _pausedIcon : _activeIcon;
        _icon.Text = _controller.IsPaused ? "Win Deutsch — пауза" : "Win Deutsch";
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left)
            return;
        if (_controller.Settings.Mode == DisplayMode.Widget)
            _controller.ToggleWidgetHidden();
        else
            _controller.ShowNow();
    }

    private void BuildMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Показать фразу сейчас", null, (_, _) => _controller.ShowNow());

        var mode = new Forms.ToolStripMenuItem("Режим");
        mode.DropDownItems.Add(Check("Виджет", _controller.Settings.Mode == DisplayMode.Widget, () => _controller.SetMode(DisplayMode.Widget)));
        mode.DropDownItems.Add(Check("Карточка", _controller.Settings.Mode == DisplayMode.Popup, () => _controller.SetMode(DisplayMode.Popup)));
        menu.Items.Add(mode);

        if (_controller.IsPaused)
        {
            menu.Items.Add("Продолжить", null, (_, _) => _controller.Resume());
        }
        else
        {
            var pause = new Forms.ToolStripMenuItem("Пауза");
            pause.DropDownItems.Add("15 минут", null, (_, _) => _controller.Pause(PauseOption.Minutes15));
            pause.DropDownItems.Add("1 час", null, (_, _) => _controller.Pause(PauseOption.Hour1));
            pause.DropDownItems.Add("До завтра (06:00)", null, (_, _) => _controller.Pause(PauseOption.UntilTomorrow));
            pause.DropDownItems.Add("Пока не сниму", null, (_, _) => _controller.Pause(PauseOption.Indefinite));
            menu.Items.Add(pause);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Библиотека…", null, (_, _) => _controller.OpenLibrary());
        menu.Items.Add("Настройки…", null, (_, _) => _controller.OpenSettings());
        menu.Items.Add(Check("Запускать с Windows", _controller.Settings.StartWithWindows, () =>
        {
            _controller.Settings.StartWithWindows = !_controller.Settings.StartWithWindows;
            _controller.ApplySettings();
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => _controller.Exit());
    }

    private static Forms.ToolStripMenuItem Check(string text, bool isChecked, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { Checked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static Drawing.Icon DrawIcon(Drawing.Color fill)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var brush = new Drawing.SolidBrush(fill);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Drawing.Font("Segoe UI", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString("D", font, Drawing.Brushes.Black, new Drawing.RectangleF(0, 1, 32, 32), format);
        }
        var handle = bitmap.GetHicon();
        // Clone owns its own handle, so the GDI handle from GetHicon can be released right away.
        using var temp = Drawing.Icon.FromHandle(handle);
        var icon = (Drawing.Icon)temp.Clone();
        NativeMethods.DestroyIcon(handle);
        return icon;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
    }
}
