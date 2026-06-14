using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StorageChecker.Core.Storage;

namespace StorageChecker.App.ViewModels;

/// <summary>
/// ViewModel tab Dashboard: agregasi added/deleted/net per periode
/// dan data siap-plot untuk ScottPlot.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly EventDatabase _db;

    public ObservableCollection<string> Ranges { get; } = new()
    {
        "Hari ini",
        "7 hari",
        "30 hari",
        "1 tahun"
    };

    [ObservableProperty] private string _selectedRange = "7 hari";
    [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-6);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _totalAddedText = "0 B";
    [ObservableProperty] private string _totalDeletedText = "0 B";
    [ObservableProperty] private string _netChangeText = "0 B";

    // Data untuk chart bar/line per hari.
    [ObservableProperty] private DateTime[] _dailyDates = Array.Empty<DateTime>();
    [ObservableProperty] private double[] _dailyAdded = Array.Empty<double>();
    [ObservableProperty] private double[] _dailyDeleted = Array.Empty<double>();
    [ObservableProperty] private double[] _dailyNet = Array.Empty<double>();

    // Data untuk breakdown per kategori.
    [ObservableProperty] private string[] _categoryLabels = Array.Empty<string>();
    [ObservableProperty] private double[] _categoryAdded = Array.Empty<double>();
    [ObservableProperty] private double[] _categoryDeleted = Array.Empty<double>();

    public DashboardViewModel(EventDatabase db)
    {
        _db = db;
        Load();
    }

    partial void OnSelectedRangeChanged(string value) => SetRangeAndLoad(value);

    private void SetRangeAndLoad(string range)
    {
        var today = DateTime.Today;
        (StartDate, EndDate) = range switch
        {
            "Hari ini" => (today, today),
            "7 hari" => (today.AddDays(-6), today),
            "30 hari" => (today.AddDays(-29), today),
            "1 tahun" => (today.AddYears(-1).AddDays(1), today),
            _ => (today.AddDays(-6), today)
        };
        Load();
    }

    [RelayCommand]
    public void Load()
    {
        var start = DateOnly.FromDateTime(StartDate);
        var end = DateOnly.FromDateTime(EndDate);

        var stats = _db.GetStats(start, end);
        TotalAddedText = FileEventViewModel.HumanSize(stats.TotalAddedBytes);
        TotalDeletedText = FileEventViewModel.HumanSize(stats.TotalDeletedBytes);
        NetChangeText = FileEventViewModel.HumanSize(stats.NetChangeBytes);

        var daily = _db.GetDailyStats(start, end);
        DailyDates = daily.Select(d => d.Date.ToDateTime(TimeOnly.MinValue)).ToArray();
        DailyAdded = daily.Select(d => (double)d.AddedBytes).ToArray();
        DailyDeleted = daily.Select(d => (double)d.DeletedBytes).ToArray();
        DailyNet = daily.Select(d => (double)d.NetBytes).ToArray();

        var cats = _db.GetCategoryStats(start, end);
        CategoryLabels = cats.Select(c => CategoryLabel(c.Category)).ToArray();
        CategoryAdded = cats.Select(c => (double)c.AddedBytes).ToArray();
        CategoryDeleted = cats.Select(c => (double)c.DeletedBytes).ToArray();
    }

    [RelayCommand]
    public void Previous()
    {
        var span = EndDate - StartDate;
        StartDate = StartDate.AddDays(-span.TotalDays - 1);
        EndDate = EndDate.AddDays(-span.TotalDays - 1);
        Load();
    }

    [RelayCommand]
    public void Next()
    {
        var span = EndDate - StartDate;
        StartDate = StartDate.AddDays(span.TotalDays + 1);
        EndDate = EndDate.AddDays(span.TotalDays + 1);
        if (EndDate > DateTime.Today)
        {
            EndDate = DateTime.Today;
            SetRangeAndLoad(SelectedRange);
            return;
        }
        Load();
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
