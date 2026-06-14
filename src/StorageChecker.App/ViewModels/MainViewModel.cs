using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
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
    public ICollectionView EventsView { get; }
    public List<string> SortOptions { get; } = new()
    {
        "Waktu ↓",
        "Ukuran ↑",
        "Ukuran ↓",
        "Kategori",
        "Drive"
    };

    public HistoryViewModel History { get; }
    public DashboardViewModel Dashboard { get; }

    [ObservableProperty] private string _statusText = "Memulai...";
    [ObservableProperty] private long _totalAddedToday;
    [ObservableProperty] private long _totalDeletedToday;
    [ObservableProperty] private FileEventViewModel? _selected;
    [ObservableProperty] private bool _autoStartEnabled;
    [ObservableProperty] private string _selectedSort = "Waktu ↓";

    public string TotalAddedText => FileEventViewModel.HumanSize(TotalAddedToday);
    public string TotalDeletedText => FileEventViewModel.HumanSize(TotalDeletedToday);
    public string NetDeltaText => FileEventViewModel.HumanSize(TotalAddedToday - TotalDeletedToday);
    public Brush NetDeltaBrush => (TotalAddedToday - TotalDeletedToday) >= 0 ? Brushes.DarkRed : Brushes.DarkGreen;

    public MainViewModel(MonitorService monitor, FileOperations fileOps, EventDatabase db)
    {
        _monitor = monitor;
        _fileOps = fileOps;
        _dispatcher = Application.Current.Dispatcher;
        History = new HistoryViewModel(db);
        Dashboard = new DashboardViewModel(db);

        EventsView = CollectionViewSource.GetDefaultView(Events);
        EventsView.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
        if (EventsView is ListCollectionView liveView)
        {
            liveView.IsLiveSorting = true;
            liveView.LiveSortingProperties.Add("Time");
            liveView.LiveSortingProperties.Add("Model.DeltaBytes");
            liveView.LiveSortingProperties.Add("CategoryText");
            liveView.LiveSortingProperties.Add("Drive");
        }

        // Inisialisasi state checkbox dari kondisi Task Scheduler saat ini.
        _suppressAutoStartChange = true;
        try { AutoStartEnabled = _autoStart.IsEnabled(); } catch { }
        _suppressAutoStartChange = false;
    }

    partial void OnSelectedSortChanged(string value) => ApplySort(value);

    private void ApplySort(string sort)
    {
        EventsView.SortDescriptions.Clear();
        var (prop, dir) = sort switch
        {
            "Ukuran ↑" => ("Model.DeltaBytes", ListSortDirection.Ascending),
            "Ukuran ↓" => ("Model.DeltaBytes", ListSortDirection.Descending),
            "Kategori" => ("CategoryText", ListSortDirection.Ascending),
            "Drive" => ("Drive", ListSortDirection.Ascending),
            _ => ("Time", ListSortDirection.Descending)
        };
        EventsView.SortDescriptions.Add(new SortDescription(prop, dir));
        EventsView.Refresh();
    }

    partial void OnTotalAddedTodayChanged(long value) => OnPropertyChanged(nameof(NetDeltaText));
    partial void OnTotalDeletedTodayChanged(long value) => OnPropertyChanged(nameof(NetDeltaText));

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

        if (vm.Model.EventType == FileEventType.Deleted)
            TotalDeletedToday += Math.Abs(vm.Model.DeltaBytes);
        else
            TotalAddedToday += vm.Model.DeltaBytes;

        OnPropertyChanged(nameof(TotalAddedText));
        OnPropertyChanged(nameof(TotalDeletedText));

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
