using System.Threading;
using System.Windows;
using StorageChecker.App.Services;
using StorageChecker.App.ViewModels;
using StorageChecker.Core;
using StorageChecker.Core.Storage;

namespace StorageChecker.App;

/// <summary>
/// Entry point. Wiring: single-instance, tray-first lifecycle, monitor service.
/// Window utama dibuat lazy (hanya saat user buka dashboard) lalu di-Hide,
/// sehingga app ringan saat idle di tray.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "StorageCheckerRealtime_SingleInstance";
    private Mutex? _mutex;

    private EventDatabase? _db;
    private MonitorService? _monitor;
    private TrayIconService? _tray;
    private FileOperations? _fileOps;
    private MainViewModel? _viewModel;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance guard.
        _mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            MessageBox.Show("Storage Checker sudah berjalan (cek system tray).",
                "Storage Checker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Jangan tutup app saat window terakhir ditutup — hidup di tray.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Core services.
        _db = new EventDatabase();
        _fileOps = new FileOperations();
        _monitor = new MonitorService(new[] { 'C', 'D', 'E' }, _db);
        _monitor.Start();

        _viewModel = new MainViewModel(_monitor, _fileOps, _db);
        _viewModel.Start();

        // Tray.
        _tray = new TrayIconService();
        _tray.Initialize();
        _tray.OpenDashboardRequested += ShowDashboard;
        _tray.ExitRequested += ExitApp;

        // Purge retensi (30 hari) di background.
        Task.Run(() =>
        {
            try { _db.PurgeOlderThan(DateTime.UtcNow.AddDays(-30)); } catch { }
        });

        // Jika TIDAK dijalankan oleh scheduler (--tray), tampilkan dashboard sekali.
        var startedByScheduler = e.Args.Contains("--tray");
        if (!startedByScheduler)
            ShowDashboard();
    }

    private void ShowDashboard()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, args) =>
            {
                // Tutup = sembunyikan ke tray, bukan exit.
                args.Cancel = true;
                _window.Hide();
            };
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _monitor?.Dispose();
        _db?.Dispose();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Shutdown();
    }
}
