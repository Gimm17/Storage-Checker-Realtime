using System.Drawing;
using System.IO;
using System.Reflection;
using WinForms = System.Windows.Forms;

namespace StorageChecker.App.Services;

/// <summary>
/// System tray icon berbasis WinForms NotifyIcon — paling andal di .NET 8.
/// Menjaga app hidup di latar belakang meski window utama disembunyikan.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private WinForms.NotifyIcon? _tray;

    public event Action? OpenDashboardRequested;
    public event Action? ExitRequested;

    public void Initialize()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "Storage Checker Realtime",
            Icon = LoadIcon(),
            Visible = true
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Buka Dashboard", null, (_, _) => OpenDashboardRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => ExitRequested?.Invoke());
        _tray.ContextMenuStrip = menu;

        // Double-click kiri membuka dashboard.
        _tray.DoubleClick += (_, _) => OpenDashboardRequested?.Invoke();
    }

    /// <summary>Tampilkan balloon notification (mis. lonjakan storage besar).</summary>
    public void ShowBalloon(string title, string message)
    {
        if (_tray is null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = message;
        _tray.ShowBalloonTip(5000);
    }

    private static Icon LoadIcon()
    {
        // Ambil app.ico yang ter-embed/terkopi di folder output.
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var icoPath = Path.Combine(baseDir, "Assets", "app.ico");
            if (File.Exists(icoPath))
                return new Icon(icoPath);
        }
        catch { }

        // Fallback: ikon dari exe sendiri, lalu ikon sistem.
        try
        {
            var exe = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return extracted;
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
