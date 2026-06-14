using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CommunityToolkit.Mvvm.ComponentModel;
using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;
using StorageChecker.Core.Storage;

namespace StorageChecker.App.ViewModels;

/// <summary>Wrapper tampilan untuk satu FileEvent — menambah properti format-siap-UI.</summary>
public sealed partial class FileEventViewModel : ObservableObject
{
    public FileEventViewModel(FileEvent model)
    {
        Model = model;
    }

    public FileEvent Model { get; }

    public DateTime Time => Model.TimestampUtc.ToLocalTime();
    public char Drive => Model.Drive;
    public string FileName => Model.FileName;
    public string FullPath => Model.FullPath;
    public FileCategory Category => Model.Category;
    public SafetyLevel Safety => Model.Safety;
    public FileEventType EventType => Model.EventType;

    [ObservableProperty]
    private bool _isDeleted;

    public string SizeText => HumanSize(Model.SizeBytes);
    public string DeltaText => HumanSize(Math.Abs(Model.DeltaBytes));

    public string DeltaTextSigned => Model.EventType switch
    {
        FileEventType.Deleted => $"-{HumanSize(Math.Abs(Model.DeltaBytes))}",
        _ => $"+{HumanSize(Model.DeltaBytes)}"
    };

    public Brush DeltaBrush => Model.EventType switch
    {
        FileEventType.Deleted => DeletedBrush,
        _ => AddedBrush
    };

    public string CategoryText => Model.Category switch
    {
        FileCategory.BrowserCache => "Cache Browser",
        FileCategory.DevDependencies => "Dependencies Dev",
        FileCategory.WindowsUpdate => "Windows Update",
        FileCategory.AppInstaller => "Installer/App",
        FileCategory.System => "Sistem",
        FileCategory.Logs => "Log",
        FileCategory.Media => "Media",
        FileCategory.Game => "Game",
        FileCategory.CloudSync => "OneDrive",
        _ => "Tak Dikenal"
    };

    public string SafetyText => Model.Safety switch
    {
        SafetyLevel.Safe => "Aman",
        SafetyLevel.Caution => "Hati-hati",
        _ => "BERBAHAYA"
    };

    public static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = Math.Abs(bytes);
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.##} {units[u]}";
    }

    private static readonly Brush AddedBrush = Brushes.DarkGreen;
    private static readonly Brush DeletedBrush = Brushes.DodgerBlue;
}
