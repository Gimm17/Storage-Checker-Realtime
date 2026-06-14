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

    [Fact]
    public void Event_type_roundtrip()
    {
        var events = new List<FileEvent>
        {
            new() { TimestampUtc = DateTime.UtcNow, Drive = 'C', FullPath = @"C:\added.txt",
                    FileName = "added.txt", SizeBytes = 100, DeltaBytes = 100,
                    Category = FileCategory.Logs, Safety = SafetyLevel.Safe,
                    EventType = FileEventType.Added },
            new() { TimestampUtc = DateTime.UtcNow, Drive = 'C', FullPath = @"C:\deleted.txt",
                    FileName = "deleted.txt", SizeBytes = 50, DeltaBytes = -50,
                    Category = FileCategory.BrowserCache, Safety = SafetyLevel.Safe,
                    EventType = FileEventType.Deleted, IsDeleted = true },
        };
        _db.InsertEvents(events);

        var files = _db.GetTopFilesForDate(DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EventType == FileEventType.Added);
        Assert.Contains(files, f => f.EventType == FileEventType.Deleted);
    }

    [Fact]
    public void GetStats_aggregates_added_deleted_and_net()
    {
        var today = DateTime.UtcNow;
        _db.InsertEvents(new List<FileEvent>
        {
            new() { TimestampUtc = today, Drive = 'C', FullPath = @"C:\a.txt",
                    FileName = "a.txt", SizeBytes = 1000, DeltaBytes = 1000,
                    Category = FileCategory.Logs, EventType = FileEventType.Added },
            new() { TimestampUtc = today, Drive = 'C', FullPath = @"C:\b.txt",
                    FileName = "b.txt", SizeBytes = 300, DeltaBytes = -300,
                    Category = FileCategory.BrowserCache, EventType = FileEventType.Deleted, IsDeleted = true },
        });

        var stats = _db.GetStats(DateOnly.FromDateTime(today.ToLocalTime()), DateOnly.FromDateTime(today.ToLocalTime()));
        Assert.Equal(1000, stats.TotalAddedBytes);
        Assert.Equal(300, stats.TotalDeletedBytes);
        Assert.Equal(700, stats.NetChangeBytes);
        Assert.Equal(2, stats.EventCount);
    }

    [Fact]
    public void GetDailyStats_groups_by_day()
    {
        var baseUtc = DateTime.UtcNow.Date.AddDays(-1);
        _db.InsertEvents(new List<FileEvent>
        {
            new() { TimestampUtc = baseUtc.AddHours(10), Drive = 'C', FullPath = @"C:\a.txt",
                    FileName = "a.txt", SizeBytes = 500, DeltaBytes = 500,
                    Category = FileCategory.Logs, EventType = FileEventType.Added },
            new() { TimestampUtc = baseUtc.AddHours(14), Drive = 'C', FullPath = @"C:\b.txt",
                    FileName = "b.txt", SizeBytes = 200, DeltaBytes = -200,
                    Category = FileCategory.Logs, EventType = FileEventType.Deleted, IsDeleted = true },
            new() { TimestampUtc = baseUtc.AddDays(1).AddHours(8), Drive = 'C', FullPath = @"C:\c.txt",
                    FileName = "c.txt", SizeBytes = 100, DeltaBytes = 100,
                    Category = FileCategory.Logs, EventType = FileEventType.Added },
        });

        var localBase = DateOnly.FromDateTime(baseUtc.ToLocalTime());
        var daily = _db.GetDailyStats(localBase, localBase.AddDays(1));
        Assert.Equal(2, daily.Count);
        Assert.Equal(500, daily[0].AddedBytes);
        Assert.Equal(200, daily[0].DeletedBytes);
        Assert.Equal(100, daily[1].AddedBytes);
    }

    [Fact]
    public void GetCategoryStats_groups_by_category()
    {
        var today = DateTime.UtcNow;
        _db.InsertEvents(new List<FileEvent>
        {
            new() { TimestampUtc = today, Drive = 'C', FullPath = @"C:\a.txt",
                    FileName = "a.txt", SizeBytes = 400, DeltaBytes = 400,
                    Category = FileCategory.Logs, EventType = FileEventType.Added },
            new() { TimestampUtc = today, Drive = 'C', FullPath = @"C:\b.txt",
                    FileName = "b.txt", SizeBytes = 100, DeltaBytes = -100,
                    Category = FileCategory.Logs, EventType = FileEventType.Deleted, IsDeleted = true },
            new() { TimestampUtc = today, Drive = 'C', FullPath = @"C:\c.txt",
                    FileName = "c.txt", SizeBytes = 600, DeltaBytes = 600,
                    Category = FileCategory.Media, EventType = FileEventType.Added },
        });

        var cats = _db.GetCategoryStats(DateOnly.FromDateTime(today.ToLocalTime()), DateOnly.FromDateTime(today.ToLocalTime()));
        Assert.Equal(2, cats.Count);
        var logs = cats.First(c => c.Category == FileCategory.Logs);
        Assert.Equal(400, logs.AddedBytes);
        Assert.Equal(100, logs.DeletedBytes);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
