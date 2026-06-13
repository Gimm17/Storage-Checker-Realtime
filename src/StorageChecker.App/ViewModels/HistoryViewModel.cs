using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageChecker.Core.Storage;

namespace StorageChecker.App.ViewModels;

/// <summary>Baris ringkasan kategori untuk satu tanggal (tab Riwayat).</summary>
public sealed class SummaryRow
{
    public required string Drive { get; init; }
    public required string Category { get; init; }
    public required string Total { get; init; }
    public int FileCount { get; init; }
}

/// <summary>
/// ViewModel tab Riwayat: pilih tanggal → tampilkan total pertambahan per
/// drive+kategori dan daftar file terbesar hari itu (dari SQLite).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly EventDatabase _db;

    public ObservableCollection<SummaryRow> Summary { get; } = new();
    public ObservableCollection<FileEventViewModel> TopFiles { get; } = new();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _grandTotalText = "0 B";

    public HistoryViewModel(EventDatabase db)
    {
        _db = db;
    }

    partial void OnSelectedDateChanged(DateTime value) => Load();

    [RelayCommand]
    public void Load()
    {
        Summary.Clear();
        TopFiles.Clear();

        var date = DateOnly.FromDateTime(SelectedDate);
        long grand = 0;

        foreach (var s in _db.GetDailySummary(date))
        {
            grand += s.TotalBytes;
            Summary.Add(new SummaryRow
            {
                Drive = s.Drive + ":",
                Category = CategoryLabel(s.Category),
                Total = FileEventViewModel.HumanSize(s.TotalBytes),
                FileCount = s.FileCount
            });
        }

        foreach (var f in _db.GetTopFilesForDate(date))
            TopFiles.Add(new FileEventViewModel(f));

        GrandTotalText = FileEventViewModel.HumanSize(grand);
    }

    private static string CategoryLabel(Core.Categorization.FileCategory c) => c switch
    {
        Core.Categorization.FileCategory.BrowserCache => "Cache Browser",
        Core.Categorization.FileCategory.DevDependencies => "Dependencies Dev",
        Core.Categorization.FileCategory.WindowsUpdate => "Windows Update",
        Core.Categorization.FileCategory.AppInstaller => "Installer/App",
        Core.Categorization.FileCategory.System => "Sistem",
        Core.Categorization.FileCategory.Logs => "Log",
        Core.Categorization.FileCategory.Media => "Media",
        Core.Categorization.FileCategory.Game => "Game",
        Core.Categorization.FileCategory.CloudSync => "OneDrive",
        _ => "Tak Dikenal"
    };
}
