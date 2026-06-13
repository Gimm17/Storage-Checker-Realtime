using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StorageChecker.Core.Usn;

/// <summary>
/// Membaca NTFS USN Change Journal untuk satu volume.
/// Membuka handle volume, query journal metadata, lalu membaca record perubahan
/// sejak USN cursor tertentu (mendukung catch-up setelah restart).
/// </summary>
public sealed class UsnJournalReader : IDisposable
{
    private readonly char _drive;
    private SafeFileHandle _volumeHandle = null!;
    private PathResolver _pathResolver = null!;
    private ulong _journalId;

    public UsnJournalReader(char drive)
    {
        _drive = char.ToUpperInvariant(drive);
    }

    public ulong JournalId => _journalId;

    /// <summary>
    /// Buka handle volume & query journal. Lempar exception jak volume bukan NTFS
    /// atau journal tidak aktif. Mengembalikan NextUsn saat ini (titik akhir journal).
    /// </summary>
    public long Open()
    {
        var volumePath = $@"\\.\{_drive}:";
        _volumeHandle = UsnNative.CreateFileW(
            volumePath,
            UsnNative.GENERIC_READ,
            UsnNative.FILE_SHARE_READ | UsnNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            UsnNative.OPEN_EXISTING,
            UsnNative.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (_volumeHandle.IsInvalid)
            throw new IOException(
                $"Gagal membuka volume {_drive}: (error {Marshal.GetLastWin32Error()}). Butuh hak admin.");

        var journal = QueryJournal();
        _journalId = journal.UsnJournalID;
        _pathResolver = new PathResolver(_volumeHandle, _drive);
        return journal.NextUsn;
    }

    private UsnNative.USN_JOURNAL_DATA_V0 QueryJournal()
    {
        var size = Marshal.SizeOf<UsnNative.USN_JOURNAL_DATA_V0>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!UsnNative.DeviceIoControl(
                    _volumeHandle, UsnNative.FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero))
            {
                throw new IOException(
                    $"USN Journal tidak aktif di {_drive}: (error {Marshal.GetLastWin32Error()}).");
            }
            return Marshal.PtrToStructure<UsnNative.USN_JOURNAL_DATA_V0>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Baca record perubahan mulai dari <paramref name="startUsn"/>.
    /// Mengembalikan record terparse + memperbarui startUsn ke posisi berikutnya (via out).
    /// Buffer 64KB per panggilan; pemanggil loop sampai nextUsn tidak maju lagi.
    /// </summary>
    public IReadOnlyList<UsnRecord> Read(long startUsn, out long nextUsn)
    {
        const int bufferSize = 64 * 1024;
        var input = new UsnNative.READ_USN_JOURNAL_DATA_V0
        {
            StartUsn = startUsn,
            ReasonMask = UsnNative.USN_REASON_DATA_EXTEND
                       | UsnNative.USN_REASON_FILE_CREATE
                       | UsnNative.USN_REASON_FILE_DELETE
                       | UsnNative.USN_REASON_RENAME_NEW_NAME
                       | UsnNative.USN_REASON_CLOSE,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0,
            UsnJournalID = _journalId
        };

        var inputSize = Marshal.SizeOf<UsnNative.READ_USN_JOURNAL_DATA_V0>();
        var inputPtr = Marshal.AllocHGlobal(inputSize);
        var outputPtr = Marshal.AllocHGlobal(bufferSize);
        var results = new List<UsnRecord>();
        nextUsn = startUsn;

        try
        {
            Marshal.StructureToPtr(input, inputPtr, false);

            if (!UsnNative.DeviceIoControl(
                    _volumeHandle, UsnNative.FSCTL_READ_USN_JOURNAL,
                    inputPtr, (uint)inputSize, outputPtr, bufferSize,
                    out var bytesReturned, IntPtr.Zero))
            {
                // Tidak ada data baru atau error sementara — kembalikan kosong.
                return results;
            }

            if (bytesReturned < sizeof(long))
                return results;

            // 8 byte pertama = USN berikutnya untuk panggilan selanjutnya.
            nextUsn = Marshal.ReadInt64(outputPtr);
            ParseRecords(outputPtr, bytesReturned, results);
            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
            Marshal.FreeHGlobal(outputPtr);
        }
    }

    private void ParseRecords(IntPtr buffer, uint bytesReturned, List<UsnRecord> results)
    {
        var headerSize = Marshal.SizeOf<UsnNative.USN_RECORD_V2_HEADER>();
        var offset = sizeof(long); // lewati field NextUsn di awal buffer

        while (offset < bytesReturned)
        {
            var recordPtr = IntPtr.Add(buffer, offset);
            var header = Marshal.PtrToStructure<UsnNative.USN_RECORD_V2_HEADER>(recordPtr);

            if (header.RecordLength == 0)
                break;

            // Hanya proses V2 (paling umum). Lewati versi lain dengan aman.
            if (header.MajorVersion == 2 && header.FileNameLength > 0)
            {
                var name = Marshal.PtrToStringUni(
                    IntPtr.Add(recordPtr, header.FileNameOffset),
                    header.FileNameLength / 2);

                results.Add(new UsnRecord
                {
                    FileReferenceNumber = header.FileReferenceNumber,
                    ParentFileReferenceNumber = header.ParentFileReferenceNumber,
                    Usn = header.Usn,
                    FileName = name ?? string.Empty,
                    Reason = header.Reason,
                    FileAttributes = header.FileAttributes,
                    TimestampUtc = DateTime.FromFileTimeUtc(header.TimeStamp),
                    Drive = _drive
                });
            }

            offset += (int)header.RecordLength;
        }
    }

    /// <summary>Resolve path lengkap sebuah record (delegasi ke PathResolver).</summary>
    public string? ResolvePath(UsnRecord record) =>
        _pathResolver.Resolve(record.ParentFileReferenceNumber, record.FileName);

    public void Dispose()
    {
        _volumeHandle?.Dispose();
    }
}
