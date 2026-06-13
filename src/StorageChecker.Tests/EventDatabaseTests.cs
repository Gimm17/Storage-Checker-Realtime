using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;
using StorageChecker.Core.Storage;

namespace StorageChecker.Tests;

public class EventDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly EventDatabase _db;

    public EventDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sc_test_{Guid.NewGuid():N}.db");
        _db = new EventDatabase(_dbPath);
    }

    [Fact]
    public void Insert_and_count_events()
    {
        var events = new List<FileEvent>
        {
            new() { TimestampUtc = DateTime.UtcNow, Drive = 'C', FullPath = @"C:\a.txt",
                    FileName = "a.txt", SizeBytes = 100, DeltaBytes = 100,
                    Category = FileCategory.Logs, Safety = SafetyLevel.Safe },
            new() { TimestampUtc = DateTime.UtcNow, Drive = 'D', FullPath = @"D:\b.mkv",
                    FileName = "b.mkv", SizeBytes = 5000, DeltaBytes = 5000,
                    Category = FileCategory.Media, Safety = SafetyLevel.Caution },
        };
        _db.InsertEvents(events);
        Assert.Equal(2, _db.CountEvents());
    }

    [Fact]
    public void Cursor_roundtrip()
    {
        var cursor = new VolumeCursor { Drive = 'C', JournalId = 12345UL, LastUsn = 99999L };
        _db.SaveCursor(cursor);

        var loaded = _db.GetCursor('C');
        Assert.NotNull(loaded);
        Assert.Equal(12345UL, loaded!.JournalId);
        Assert.Equal(99999L, loaded.LastUsn);
    }

    [Fact]
    public void Cursor_upsert_overwrites()
    {
        _db.SaveCursor(new VolumeCursor { Drive = 'C', JournalId = 1, LastUsn = 10 });
        _db.SaveCursor(new VolumeCursor { Drive = 'C', JournalId = 1, LastUsn = 20 });
        Assert.Equal(20L, _db.GetCursor('C')!.LastUsn);
    }

    [Fact]
    public void Purge_removes_old_events()
    {
        _db.InsertEvents(new List<FileEvent>
        {
            new() { TimestampUtc = DateTime.UtcNow.AddDays(-40), Drive = 'C',
                    FullPath = @"C:\old.txt", FileName = "old.txt", SizeBytes = 1, DeltaBytes = 1 },
        });
        var removed = _db.PurgeOlderThan(DateTime.UtcNow.AddDays(-30));
        Assert.Equal(1, removed);
        Assert.Equal(0, _db.CountEvents());
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
