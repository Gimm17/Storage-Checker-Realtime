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
                is_deleted  INTEGER NOT NULL DEFAULT 0
            );");
        Exec("CREATE INDEX IF NOT EXISTS ix_events_ts ON file_events(ts_utc);");
        Exec("CREATE INDEX IF NOT EXISTS ix_events_cat ON file_events(category);");
        Exec(@"
            CREATE TABLE IF NOT EXISTS volume_cursors (
                drive      TEXT    PRIMARY KEY,
                journal_id INTEGER NOT NULL,
                last_usn   INTEGER NOT NULL
            );");
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
                (ts_utc, drive, full_path, file_name, size_bytes, delta_bytes, category, safety, reason, is_deleted)
            VALUES ($ts, $drive, $path, $name, $size, $delta, $cat, $safety, $reason, $del);";

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
                   category, safety, reason, is_deleted
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
                IsDeleted = r.GetInt32(9) != 0
            });
        }
        return result;
    }
}
