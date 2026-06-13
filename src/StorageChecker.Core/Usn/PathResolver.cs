using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StorageChecker.Core.Usn;

/// <summary>
/// Merekonstruksi path lengkap sebuah file dari FileReferenceNumber (FRN) NTFS.
/// USN record hanya memberi nama file + FRN induk, jadi path penuh harus dibangun
/// dengan membuka file by-id. Hasil di-cache (LRU) agar tidak resolve berulang —
/// ini krusial untuk performa karena disk C bisa ribuan event/menit.
/// </summary>
public sealed class PathResolver
{
    private readonly SafeFileHandle _volumeHandle;
    private readonly string _driveRoot; // mis. "C:\"
    private readonly LruCache<ulong, string> _cache;

    public PathResolver(SafeFileHandle volumeHandle, char drive, int cacheCapacity = 8192)
    {
        _volumeHandle = volumeHandle;
        _driveRoot = $"{drive}:\\";
        _cache = new LruCache<ulong, string>(cacheCapacity);
    }

    /// <summary>
    /// Bangun path lengkap dari nama file + FRN folder induk (keduanya tersedia di USN record).
    /// Mengembalikan null jika folder induk sudah terhapus / tidak bisa di-resolve.
    /// </summary>
    public string? Resolve(ulong parentFileReferenceNumber, string fileName)
    {
        var parentPath = ResolveDirectory(parentFileReferenceNumber);
        if (parentPath is null)
            return null;

        return Path.Combine(parentPath, fileName);
    }

    /// <summary>Resolve path sebuah folder (FRN) lengkap sampai root drive, dengan cache.</summary>
    private string? ResolveDirectory(ulong frn)
    {
        if (frn == 0)
            return _driveRoot.TrimEnd('\\');

        if (_cache.TryGet(frn, out var cached))
            return cached;

        using var handle = OpenById(frn);
        if (handle is null || handle.IsInvalid)
            return null;

        var name = GetFinalName(handle);
        if (name is null)
            return null;

        // GetFinalPathNameByHandle sudah memberi path penuh — pakai langsung & cache.
        _cache.Add(frn, name);
        return name;
    }

    private SafeFileHandle? OpenById(ulong frn)
    {
        var descriptor = new FILE_ID_DESCRIPTOR
        {
            dwSize = (uint)Marshal.SizeOf<FILE_ID_DESCRIPTOR>(),
            Type = 0, // FileIdType
            FileId = (long)frn
        };

        var handle = OpenFileById(
            _volumeHandle,
            ref descriptor,
            UsnNative.GENERIC_READ,
            UsnNative.FILE_SHARE_READ | UsnNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            UsnNative.FILE_FLAG_BACKUP_SEMANTICS);

        return handle;
    }

    private static string? GetFinalName(SafeFileHandle handle)
    {
        var sb = new StringBuilder(1024);
        var len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
        if (len == 0)
            return null;

        var result = sb.ToString();
        // Buang prefix "\\?\" yang ditambahkan API.
        if (result.StartsWith(@"\\?\"))
            result = result.Substring(4);
        return result;
    }

    // ── Interop khusus path resolution ────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_DESCRIPTOR
    {
        public uint dwSize;
        public int Type;
        public long FileId; // union; untuk FileIdType cukup 64-bit
        private readonly long _padding; // ruang untuk GUID union (128-bit)
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FILE_ID_DESCRIPTOR lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
