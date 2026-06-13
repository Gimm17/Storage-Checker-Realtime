namespace StorageChecker.Core.Safety;

/// <summary>
/// Tingkat keamanan menghapus sebuah file. Dipakai UI untuk memberi
/// warna/ikon dan menentukan apakah perlu konfirmasi ganda.
/// </summary>
public enum SafetyLevel
{
    /// <summary>Aman dihapus (cache, temp, log, build artifact).</summary>
    Safe,

    /// <summary>Boleh dihapus tapi hati-hati (app terinstall, config, dependencies aktif).</summary>
    Caution,

    /// <summary>Berbahaya — bisa merusak sistem/aplikasi. Wajib konfirmasi ganda.</summary>
    Dangerous
}
