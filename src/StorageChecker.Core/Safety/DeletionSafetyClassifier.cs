using StorageChecker.Core.Categorization;

namespace StorageChecker.Core.Safety;

/// <summary>
/// Menentukan SafetyLevel sebuah file sebelum user menghapusnya.
/// Kombinasi: lokasi path kritis OS + atribut sistem + kategori file.
/// Prinsip konservatif — jika ragu, naikkan ke level lebih aman (Caution/Dangerous).
/// </summary>
public sealed class DeletionSafetyClassifier
{
    private static readonly string[] DangerousPathTokens =
    {
        @"\windows\system32\",
        @"\windows\syswow64\",
        @"\windows\winsxs\",
        @"\windows\boot\",
        @"\windows\fonts\",
        @"\system volume information\",
        @"\$recycle.bin\",
        @"\program files\",
        @"\program files (x86)\",
        @"\programdata\microsoft\windows\",
    };

    private static readonly string[] DangerousFileNames =
    {
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "ntldr",
    };

    private static readonly string[] SafeCategoriesTokens =
    {
        @"\appdata\local\temp\", @"\windows\temp\", @"\cache\", @"\code cache\",
        @"\gpucache\", @"\cache2\", @"\inetcache\",
    };

    /// <summary>
    /// Klasifikasi berbasis path + atribut sistem + kategori.
    /// </summary>
    public SafetyLevel Classify(string fullPath, bool isSystemAttribute, FileCategory category)
    {
        if (string.IsNullOrEmpty(fullPath))
            return SafetyLevel.Caution;

        var lower = fullPath.ToLowerInvariant();
        var fileName = Path.GetFileName(lower);

        // 1) File sistem inti / nama file kritis → selalu Dangerous.
        if (DangerousFileNames.Any(f => fileName.Equals(f, StringComparison.OrdinalIgnoreCase)))
            return SafetyLevel.Dangerous;

        // 2) Atribut System dari NTFS → Dangerous (file OS/driver).
        if (isSystemAttribute)
            return SafetyLevel.Dangerous;

        // 3) Lokasi path kritis OS → Dangerous.
        //    Pengecualian: folder Temp di dalam Windows tetap aman.
        if (DangerousPathTokens.Any(t => lower.Contains(t)))
        {
            if (lower.Contains(@"\windows\temp\") || lower.Contains(@"\softwaredistribution\download\"))
                return SafetyLevel.Caution;
            return SafetyLevel.Dangerous;
        }

        // 4) Kategori yang umumnya aman dibersihkan.
        switch (category)
        {
            case FileCategory.BrowserCache:
            case FileCategory.Logs:
                return SafetyLevel.Safe;

            case FileCategory.AppInstaller:
                // Installer di Temp aman; di Downloads aman juga (user bisa unduh ulang).
                return SafetyLevel.Safe;

            case FileCategory.System:
            case FileCategory.WindowsUpdate:
                return SafetyLevel.Dangerous;

            case FileCategory.DevDependencies:
                // Bisa di-restore via npm/pip install, tapi mengganggu proyek aktif.
                return SafetyLevel.Caution;

            case FileCategory.CloudSync:
                // Menghapus bisa memicu hapus di cloud — hati-hati.
                return SafetyLevel.Caution;
        }

        // 5) Folder cache/temp yang lolos kategori → Safe.
        if (SafeCategoriesTokens.Any(t => lower.Contains(t)))
            return SafetyLevel.Safe;

        // 6) Default: Caution (lebih aman daripada Safe untuk file tak dikenal).
        return SafetyLevel.Caution;
    }
}
