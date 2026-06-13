using System.Drawing;
using System.Windows;
using H.NotifyIcon;

namespace StorageChecker.App.Services;

/// <summary>
/// Mengelola ikon system tray + context menu. Menjaga app hidup di latar belakang
/// meski window utama disembunyikan.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _tray;

    public event Action? OpenDashboardRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Storage Checker Realtime — memantau storage",
            Icon = SystemIcons.Shield // placeholder; bisa diganti ikon kustom .ico
        };

        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Buka Dashboard" };
        openItem.Click += (_, _) => OpenDashboardRequested?.Invoke();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Keluar" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);

        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => OpenDashboardRequested?.Invoke();
    }

    public void ShowBalloon(string title, string message)
    {
        _tray?.ShowNotification(title, message);
    }

    public void Dispose()
    {
        _tray?.Dispose();
    }
}
