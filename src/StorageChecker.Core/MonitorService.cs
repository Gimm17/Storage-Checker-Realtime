using System.Threading.Channels;
using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;
using StorageChecker.Core.Storage;

namespace StorageChecker.Core;

/// <summary>
/// Orchestrator: menjalankan satu VolumeMonitor per drive, mengalirkan event
/// terkategori+terklasifikasi ke (1) SQLite writer dan (2) channel untuk UI.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly char[] _drives;
    private readonly EventDatabase _db;
    private readonly FileCategorizer _categorizer = new();
    private readonly DeletionSafetyClassifier _safety = new();
    private readonly List<VolumeMonitor> _monitors = new();
    private readonly Channel<FileEvent> _uiChannel =
        Channel.CreateBounded<FileEvent>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private CancellationTokenSource? _cts;

    public MonitorService(IEnumerable<char> drives, EventDatabase db)
    {
        _drives = drives.Select(char.ToUpperInvariant).ToArray();
        _db = db;
    }

    /// <summary>Reader channel untuk UI berlangganan event realtime.</summary>
    public ChannelReader<FileEvent> Events => _uiChannel.Reader;

    public IReadOnlyList<string> StartedDrives =>
        _monitors.Select(m => m.Drive + ":").ToList();

    public void Start()
    {
        _cts = new CancellationTokenSource();
        foreach (var drive in _drives)
        {
            var monitor = new VolumeMonitor(drive, _db, _categorizer, _safety, OnEvent);
            try
            {
                monitor.Start(_cts.Token);
                _monitors.Add(monitor);
            }
            catch (Exception ex)
            {
                // Volume non-NTFS / journal mati / tanpa admin → skip, jangan crash.
                Console.Error.WriteLine($"[WARN] Skip drive {drive}: {ex.Message}");
                monitor.Dispose();
            }
        }
    }

    private void OnEvent(FileEvent e)
    {
        // Non-blocking; jika penuh, channel buang yang terlama (DropOldest).
        _uiChannel.Writer.TryWrite(e);
    }

    public void Stop()
    {
        _cts?.Cancel();
        foreach (var m in _monitors)
            m.Dispose();
        _monitors.Clear();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
