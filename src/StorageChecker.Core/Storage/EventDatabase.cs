using Microsoft.Data.Sqlite;
using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;

namespace StorageChecker.Core.Storage;

/// <summary>
/// Penyimpanan riwayat event ke SQLite (WAL mode, batched insert).
/// DB default di %LOCALAPPDATA%\StorageChecker\history.db.
/// </summary>
public sealed class EventDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;

    public EventDatabase(string? dbPath = null)
    {
        dbPath ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        Initialize();
    }

    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StorageChecker");
        return Path.Combine(dir, "history.db");
    }

    private void Initialize()
    {
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec(@"
            CREATE TABLE IF NOT EXISTS file_events (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts_utc      TEXT    NOT NULL,
                drive       TEXT    NOT NULL,
                full_path   TEXT    NOT NULL,
                file_name   TEXT    NOT NULL,
                size_bytes  INTEGER NOT NULL,
                delta_bytes INTEGER NOT NULL,
                category    INTEGER NOT NULL,
                safety      INTEGER NOT NULL,
                reason      INTEGER NOT NULL,
                is_deleted  INTEGER NOT NULL DEFAULT 0,
                event_type  INTEGER NOT NULL DEFAULT 0
            );");
        Exec("CREATE INDEX IF NOT EXISTS ix_events_ts ON file_events(ts_utc);");
        Exec("CREATE INDEX IF NOT EXISTS ix_events_cat ON file_events(category);");
        Exec(@"
            CREATE TABLE IF NOT EXISTS volume_cursors (
                drive      TEXT    PRIMARY KEY,
                journal_id INTEGER NOT NULL,
                last_usn   INTEGER NOT NULL
            );");

        // Migrasi otomatis untuk DB lama yang belum punya kolom event_type.
        MigrateAddEventType();
    }

    private void MigrateAddEventType()
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT event_type FROM file_events LIMIT 1;";
            cmd.ExecuteScalar();
        }
        catch (SqliteException)
        {
            // Kolom belum ada — tambahkan.
            Exec("ALTER TABLE file_events ADD COLUMN event_type INTEGER NOT NULL DEFAULT 0;");
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    // ── Insert batch event (1 transaksi untuk banyak baris) ───────────
    public void InsertEvents(IReadOnlyList<FileEvent> events)
    {
        if (events.Count == 0) return;

        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO file_events
                (ts_utc, drive, full_path, file_name, size_bytes, delta_bytes, category, safety, reason, is_deleted, event_type)
            VALUES ($ts, $drive, $path, $name, $size, $delta, $cat, $safety, $reason, $del, $et);";

        var pTs = cmd.CreateParameter(); pTs.ParameterName = "$ts"; cmd.Parameters.Add(pTs);
        var pDrive = cmd.CreateParameter(); pDrive.ParameterName = "$drive"; cmd.Parameters.Add(pDrive);
        var pPath = cmd.CreateParameter(); pPath.ParameterName = "$path"; cmd.Parameters.Add(pPath);
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pSize = cmd.CreateParameter(); pSize.ParameterName = "$size"; cmd.Parameters.Add(pSize);
        var pDelta = cmd.CreateParameter(); pDelta.ParameterName = "$delta"; cmd.Parameters.Add(pDelta);
        var pCat = cmd.CreateParameter(); pCat.ParameterName = "$cat"; cmd.Parameters.Add(pCat);
        var pSafety = cmd.CreateParameter(); pSafety.ParameterName = "$safety"; cmd.Parameters.Add(pSafety);
        var pReason = cmd.CreateParameter(); pReason.ParameterName = "$reason"; cmd.Parameters.Add(pReason);
        var pDel = cmd.CreateParameter(); pDel.ParameterName = "$del"; cmd.Parameters.Add(pDel);
        var pEt = cmd.CreateParameter(); pEt.ParameterName = "$et"; cmd.Parameters.Add(pEt);

        foreach (var e in events)
        {
            pTs.Value = e.TimestampUtc.ToString("O");
            pDrive.Value = e.Drive.ToString();
            pPath.Value = e.FullPath;
            pName.Value = e.FileName;
            pSize.Value = e.SizeBytes;
            pDelta.Value = e.DeltaBytes;
            pCat.Value = (int)e.Category;
            pSafety.Value = (int)e.Safety;
            pReason.Value = e.Reason;
            pDel.Value = e.IsDeleted ? 1 : 0;
            pEt.Value = (int)e.EventType;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ── Cursor USN per volume ─────────────────────────────────────────
    public void SaveCursor(VolumeCursor cursor)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO volume_cursors (drive, journal_id, last_usn)
            VALUES ($d, $j, $u)
            ON CONFLICT(drive) DO UPDATE SET journal_id=$j, last_usn=$u;";
        cmd.Parameters.AddWithValue("$d", cursor.Drive.ToString());
        cmd.Parameters.AddWithValue("$j", (long)cursor.JournalId);
        cmd.Parameters.AddWithValue("$u", cursor.LastUsn);
        cmd.ExecuteNonQuery();
    }

    public VolumeCursor? GetCursor(char drive)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT journal_id, last_usn FROM volume_cursors WHERE drive=$d;";
        cmd.Parameters.AddWithValue("$d", drive.ToString());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new VolumeCursor
        {
            Drive = drive,
            JournalId = (ulong)r.GetInt64(0),
            LastUsn = r.GetInt64(1)
        };
    }

    // ── Purge event lama (retensi) ────────────────────────────────────
    public int PurgeOlderThan(DateTime cutoffUtc)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM file_events WHERE ts_utc < $c;";
        cmd.Parameters.AddWithValue("$c", cutoffUtc.ToString("O"));
        return cmd.ExecuteNonQuery();
    }

    public long CountEvents()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_events;";
        return (long)cmd.ExecuteScalar()!;
    }

    // ── Statistik agregat untuk Dashboard ───────────────────────────────

    /// <summary>
    /// Total added, deleted, dan net untuk rentang tanggal lokal [start, end].
    /// </summary>
    public UsageStats GetStats(DateOnly startLocal, DateOnly endLocal)
    {
        var startUtc = startLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var endUtc = endLocal.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(SUM(CASE WHEN event_type = 0 THEN delta_bytes ELSE 0 END), 0) AS added,
                   COALESCE(SUM(CASE WHEN event_type = 1 THEN -delta_bytes ELSE 0 END), 0) AS deleted,
                   COUNT(*) AS cnt
            FROM file_events
            WHERE ts_utc >= $start AND ts_utc < $end;";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));

        using var r = cmd.ExecuteReader();
        r.Read();
        var added = r.GetInt64(0);
        var deleted = r.GetInt64(1);
        return new UsageStats
        {
            StartDate = startLocal,
            EndDate = endLocal,
            TotalAddedBytes = added,
            TotalDeletedBytes = deleted,
            NetChangeBytes = added - deleted,
            EventCount = r.GetInt64(2)
        };
    }

    /// <summary>
    /// Statistik per hari untuk chart bar/line.
    /// </summary>
    public IReadOnlyList<DailyStat> GetDailyStats(DateOnly startLocal, DateOnly endLocal)
    {
        var startUtc = startLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var endUtc = endLocal.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT date(ts_utc) AS day,
                   COALESCE(SUM(CASE WHEN event_type = 0 THEN delta_bytes ELSE 0 END), 0) AS added,
                   COALESCE(SUM(CASE WHEN event_type = 1 THEN -delta_bytes ELSE 0 END), 0) AS deleted
            FROM file_events
            WHERE ts_utc >= $start AND ts_utc < $end
            GROUP BY day
            ORDER BY day;";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));

        var result = new List<DailyStat>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (DateTime.TryParse(r.GetString(0), out var dayUtc))
            {
                result.Add(new DailyStat
                {
                    Date = DateOnly.FromDateTime(dayUtc.ToLocalTime()),
                    AddedBytes = r.GetInt64(1),
                    DeletedBytes = r.GetInt64(2),
                    NetBytes = r.GetInt64(1) - r.GetInt64(2)
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Breakdown per kategori untuk pie/donut chart.
    /// </summary>
    public IReadOnlyList<CategoryStat> GetCategoryStats(DateOnly startLocal, DateOnly endLocal)
    {
        var startUtc = startLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var endUtc = endLocal.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT category,
                   COALESCE(SUM(CASE WHEN event_type = 0 THEN delta_bytes ELSE 0 END), 0) AS added,
                   COALESCE(SUM(CASE WHEN event_type = 1 THEN -delta_bytes ELSE 0 END), 0) AS deleted
            FROM file_events
            WHERE ts_utc >= $start AND ts_utc < $end
            GROUP BY category
            ORDER BY (added + deleted) DESC;";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));

        var result = new List<CategoryStat>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CategoryStat
            {
                Category = (FileCategory)r.GetInt32(0),
                AddedBytes = r.GetInt64(1),
                DeletedBytes = r.GetInt64(2)
            });
        }
        return result;
    }

    // ── Riwayat: ringkasan per kategori untuk satu tanggal (waktu lokal) ──
    public IReadOnlyList<DailySummary> GetDailySummary(DateOnly localDate)
    {
        // Rentang hari lokal dikonversi ke UTC untuk filter kolom ts_utc.
        var startLocal = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var startUtc = startLocal.ToUniversalTime();
        var endUtc = startLocal.AddDays(1).ToUniversalTime();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT drive, category, SUM(delta_bytes) AS total, COUNT(*) AS cnt
            FROM file_events
            WHERE ts_utc >= $start AND ts_utc < $end
            GROUP BY drive, category
            ORDER BY total DESC;";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));

        var result = new List<DailySummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new DailySummary
            {
                Date = localDate,
                Drive = r.GetString(0)[0],
                Category = (FileCategory)r.GetInt32(1),
                TotalBytes = r.GetInt64(2),
                FileCount = r.GetInt32(3)
            });
        }
        return result;
    }

    // ── Riwayat: file terbesar pada satu tanggal (untuk detail) ──
    public IReadOnlyList<FileEvent> GetTopFilesForDate(DateOnly localDate, int limit = 200)
    {
        var startLocal = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var startUtc = startLocal.ToUniversalTime();
        var endUtc = startLocal.AddDays(1).ToUniversalTime();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT ts_utc, drive, full_path, file_name, size_bytes, delta_bytes,
                   category, safety, reason, is_deleted, event_type
            FROM file_events
            WHERE ts_utc >= $start AND ts_utc < $end
            ORDER BY delta_bytes DESC
            LIMIT $lim;";
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$lim", limit);

        var result = new List<FileEvent>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new FileEvent
            {
                TimestampUtc = DateTime.Parse(r.GetString(0)).ToUniversalTime(),
                Drive = r.GetString(1)[0],
                FullPath = r.GetString(2),
                FileName = r.GetString(3),
                SizeBytes = r.GetInt64(4),
                DeltaBytes = r.GetInt64(5),
                Category = (FileCategory)r.GetInt32(6),
                Safety = (SafetyLevel)r.GetInt32(7),
                Reason = (uint)r.GetInt64(8),
                IsDeleted = r.GetInt32(9) != 0,
                EventType = (FileEventType)r.GetInt32(10)
            });
        }
        return result;
    }
}
