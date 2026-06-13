using System.Runtime.InteropServices;
using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;
using StorageChecker.Core.Storage;
using StorageChecker.Core.Usn;

namespace StorageChecker.Core;

/// <summary>
/// Memantau satu volume: loop baca USN journal, resolve path, kategorisasi,
/// klasifikasi safety, batch-insert ke DB, dan kirim ke callback UI.
/// Berjalan di thread background sendiri dengan interval polling adaptif.
/// </summary>
public sealed class VolumeMonitor : IDisposable
{
    private readonly char _drive;
    private readonly EventDatabase _db;
    private readonly FileCategorizer _categorizer;
    private readonly DeletionSafetyClassifier _safety;
    private readonly Action<FileEvent> _onEvent;
    private UsnJournalReader? _reader;
    private Task? _loop;

    private const int MinDelayMs = 1000;
    private const int MaxDelayMs = 3000;

    public VolumeMonitor(
        char drive,
        EventDatabase db,
        FileCategorizer categorizer,
        DeletionSafetyClassifier safety,
        Action<FileEvent> onEvent)
    {
        _drive = char.ToUpperInvariant(drive);
        _db = db;
        _categorizer = categorizer;
        _safety = safety;
        _onEvent = onEvent;
    }

    public char Drive => _drive;

    /// <summary>Buka journal & mulai loop. Lempar exception jika gagal buka volume.</summary>
    public void Start(CancellationToken token)
    {
        _reader = new UsnJournalReader(_drive);
        var currentEnd = _reader.Open();

        // Catch-up: lanjut dari cursor tersimpan bila journal sama; jika tidak, mulai dari ujung.
        var saved = _db.GetCursor(_drive);
        long startUsn = (saved is not null && saved.JournalId == _reader.JournalId)
            ? saved.LastUsn
            : currentEnd;

        _loop = Task.Run(() => RunLoop(startUsn, token), token);
    }

    private async Task RunLoop(long startUsn, CancellationToken token)
    {
        var usn = startUsn;
        var delay = MinDelayMs;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var records = _reader!.Read(usn, out var nextUsn);

                if (records.Count > 0)
                {
                    ProcessBatch(records);
                    usn = nextUsn;
                    _db.SaveCursor(new VolumeCursor
                    {
                        Drive = _drive,
                        JournalId = _reader.JournalId,
                        LastUsn = usn
                    });
                    delay = MinDelayMs; // ada aktivitas → cek lebih sering
                }
                else
                {
                    usn = nextUsn;
                    delay = Math.Min(delay + 500, MaxDelayMs); // sepi → perlambat
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] {_drive}: loop error: {ex.Message}");
                delay = MaxDelayMs;
            }

            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ProcessBatch(IReadOnlyList<UsnRecord> records)
    {
        var events = new List<FileEvent>(records.Count);

        foreach (var rec in records)
        {
            if (rec.IsDirectory) continue;
            // Fokus pada pertambahan data / file baru / penutupan tulis.
            if (!rec.IsDataExtend && !rec.IsCreate && !rec.IsClose) continue;

            var path = _reader!.ResolvePath(rec);
            if (path is null) continue;

            long size = TryGetFileSize(path);
            var category = _categorizer.Categorize(path);
            var safety = _safety.Classify(path, rec.IsSystem, category);

            events.Add(new FileEvent
            {
                TimestampUtc = rec.TimestampUtc,
                Drive = _drive,
                FullPath = path,
                FileName = rec.FileName,
                SizeBytes = size,
                DeltaBytes = size, // perkiraan kasar; refinasi di Fase berikut bila perlu
                Category = category,
                Safety = safety,
                Reason = rec.Reason
            });
        }

        if (events.Count == 0) return;

        _db.InsertEvents(events);
        foreach (var e in events)
            _onEvent(e);
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? fi.Length : 0;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        try { _loop?.Wait(2000); } catch { }
        _reader?.Dispose();
    }
}
