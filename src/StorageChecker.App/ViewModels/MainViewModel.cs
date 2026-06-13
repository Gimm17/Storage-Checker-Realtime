using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageChecker.App.Services;
using StorageChecker.Core;
using StorageChecker.Core.Safety;
using StorageChecker.Core.Storage;

namespace StorageChecker.App.ViewModels;

/// <summary>
/// ViewModel dashboard utama. Berlangganan event realtime dari MonitorService
/// dan menampilkannya di koleksi yang ter-bind ke DataGrid.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly MonitorService _monitor;
    private readonly FileOperations _fileOps;
    private readonly AutoStartService _autoStart = new();
    private readonly Dispatcher _dispatcher;
    private bool _suppressAutoStartChange;
    private const int MaxRows = 2000;

    public ObservableCollection<FileEventViewModel> Events { get; } = new();

    public HistoryViewModel History { get; }

    [ObservableProperty] private string _statusText = "Memulai...";
    [ObservableProperty] private long _totalDeltaToday;
    [ObservableProperty] private FileEventViewModel? _selected;
    [ObservableProperty] private bool _autoStartEnabled;

    public string TotalDeltaText => FileEventViewModel.HumanSize(TotalDeltaToday);

    public MainViewModel(MonitorService monitor, FileOperations fileOps, EventDatabase db)
    {
        _monitor = monitor;
        _fileOps = fileOps;
        _dispatcher = Application.Current.Dispatcher;
        History = new HistoryViewModel(db);

        // Inisialisasi state checkbox dari kondisi Task Scheduler saat ini.
        _suppressAutoStartChange = true;
        try { AutoStartEnabled = _autoStart.IsEnabled(); } catch { }
        _suppressAutoStartChange = false;
    }

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_suppressAutoStartChange) return;
        try
        {
            if (value) _autoStart.Enable();
            else _autoStart.Disable();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal mengubah auto-start:\n{ex.Message}\n\n" +
                            "Pastikan aplikasi berjalan sebagai Administrator.",
                "Storage Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            // Kembalikan state checkbox tanpa memicu ulang handler.
            _suppressAutoStartChange = true;
            AutoStartEnabled = !value;
            _suppressAutoStartChange = false;
        }
    }

    public void Start()
    {
        var drives = string.Join(", ", _monitor.StartedDrives);
        StatusText = drives.Length > 0
            ? $"Memantau: {drives}"
            : "Tidak ada volume NTFS yang bisa dipantau.";
        ConsumeAsync();
    }

    private async void ConsumeAsync()
    {
        await foreach (var e in _monitor.Events.ReadAllAsync())
        {
            var vm = new FileEventViewModel(e);
            _ = _dispatcher.BeginInvoke(() => AddEvent(vm));
        }
    }

    private void AddEvent(FileEventViewModel vm)
    {
        Events.Insert(0, vm);
        TotalDeltaToday += vm.Model.DeltaBytes;
        OnPropertyChanged(nameof(TotalDeltaText));

        // Batasi jumlah baris UI agar memori stabil (semua tetap ada di DB).
        while (Events.Count > MaxRows)
            Events.RemoveAt(Events.Count - 1);
    }

    [RelayCommand]
    private void OpenInExplorer(FileEventViewModel? item)
    {
        if (item is null) return;
        if (!_fileOps.OpenInExplorer(item.FullPath))
            MessageBox.Show("Tidak bisa membuka lokasi file.", "Storage Checker",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void Delete(FileEventViewModel? item)
    {
        if (item is null) return;

        // Konfirmasi ganda untuk file berbahaya.
        if (item.Safety == SafetyLevel.Dangerous)
        {
            var warn = MessageBox.Show(
                $"PERINGATAN: file ini ditandai BERBAHAYA untuk dihapus.\n\n{item.FullPath}\n\n" +
                "Menghapusnya bisa merusak Windows atau aplikasi. Yakin lanjut?",
                "Hapus File Berbahaya?",
                MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);
            if (warn != MessageBoxResult.Yes) return;
        }
        else
        {
            var confirm = MessageBox.Show(
                $"Hapus file ini ke Recycle Bin?\n\n{item.FullPath}",
                "Konfirmasi Hapus",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var (ok, error) = _fileOps.Delete(item.FullPath);
        if (ok)
        {
            item.IsDeleted = true;
        }
        else
        {
            MessageBox.Show($"Gagal menghapus:\n{error}", "Storage Checker",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
