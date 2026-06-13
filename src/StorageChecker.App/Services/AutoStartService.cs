using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;

namespace StorageChecker.App.Services;

/// <summary>
/// Mengelola auto-start saat Windows login via Task Scheduler dengan
/// "highest privileges" — agar app jalan elevated tanpa prompt UAC tiap boot
/// (Registry Run key tidak bisa elevated otomatis).
/// </summary>
public sealed class AutoStartService
{
    private const string TaskName = "StorageCheckerRealtime";

    public bool IsEnabled()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskName) is not null;
    }

    public void Enable()
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName;

        using var ts = new TaskService();
        var td = ts.NewTask();
        td.RegistrationInfo.Description =
            "Storage Checker Realtime — pantau pertambahan storage di latar belakang.";
        td.Principal.RunLevel = TaskRunLevel.Highest; // elevated tanpa UAC prompt
        td.Triggers.Add(new LogonTrigger());
        td.Actions.Add(new ExecAction(exePath, "--tray", null));

        // Jangan berhenti saat idle / di baterai.
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // tanpa batas waktu

        ts.RootFolder.RegisterTaskDefinition(TaskName, td);
    }

    public void Disable()
    {
        using var ts = new TaskService();
        if (ts.GetTask(TaskName) is not null)
            ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
    }
}
