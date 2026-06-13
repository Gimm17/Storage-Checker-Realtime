using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;

namespace StorageChecker.Core.Storage;

/// <summary>Satu baris event file yang disimpan/ditampilkan.</summary>
public sealed class FileEvent
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public char Drive { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long DeltaBytes { get; set; }
    public FileCategory Category { get; set; }
    public SafetyLevel Safety { get; set; }
    public uint Reason { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Posisi cursor USN per volume — untuk catch-up setelah restart.</summary>
public sealed class VolumeCursor
{
    public char Drive { get; set; }
    public ulong JournalId { get; set; }
    public long LastUsn { get; set; }
}

/// <summary>Agregasi harian per drive+kategori untuk view riwayat lintas hari.</summary>
public sealed class DailySummary
{
    public DateOnly Date { get; set; }
    public char Drive { get; set; }
    public FileCategory Category { get; set; }
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
}
