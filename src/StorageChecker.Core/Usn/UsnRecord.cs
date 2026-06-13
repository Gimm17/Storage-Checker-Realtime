namespace StorageChecker.Core.Usn;

/// <summary>
/// Hasil parse satu USN_RECORD_V2 yang sudah disederhanakan untuk dipakai layer atas.
/// Path lengkap diisi belakangan oleh PathResolver (USN record hanya berisi nama file + FRN induk).
/// </summary>
public sealed class UsnRecord
{
    /// <summary>Nomor referensi file NTFS (unik per volume).</summary>
    public ulong FileReferenceNumber { get; init; }

    /// <summary>Nomor referensi folder induk — dipakai merekonstruksi path lengkap.</summary>
    public ulong ParentFileReferenceNumber { get; init; }

    /// <summary>USN sekuensial record ini (posisi di journal).</summary>
    public long Usn { get; init; }

    /// <summary>Nama file/folder (tanpa path).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Bitmask alasan perubahan (USN_REASON_*).</summary>
    public uint Reason { get; init; }

    /// <summary>Atribut file (FILE_ATTRIBUTE_*), mis. directory/system/hidden.</summary>
    public uint FileAttributes { get; init; }

    /// <summary>Timestamp perubahan (UTC) dari journal.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>Huruf drive sumber (mis. 'C').</summary>
    public char Drive { get; init; }

    public bool IsDirectory =>
        (FileAttributes & UsnNative.FILE_ATTRIBUTE_DIRECTORY) != 0;

    public bool IsSystem =>
        (FileAttributes & UsnNative.FILE_ATTRIBUTE_SYSTEM) != 0;

    /// <summary>True jika record menandakan file selesai ditulis (CLOSE) — saat tepat ambil ukuran.</summary>
    public bool IsClose =>
        (Reason & UsnNative.USN_REASON_CLOSE) != 0;

    /// <summary>True jika data file bertambah (indikator kuat storage naik).</summary>
    public bool IsDataExtend =>
        (Reason & UsnNative.USN_REASON_DATA_EXTEND) != 0;

    public bool IsCreate =>
        (Reason & UsnNative.USN_REASON_FILE_CREATE) != 0;

    public bool IsDelete =>
        (Reason & UsnNative.USN_REASON_FILE_DELETE) != 0;
}
