namespace StorageChecker.Core.Categorization;

/// <summary>
/// Kategori sumber file — menjawab "file ini muncul darimana / kenapa ada".
/// Urutan dipakai juga sebagai prioritas tampilan di UI.
/// </summary>
public enum FileCategory
{
    /// <summary>Cache browser (Chrome/Edge/Firefox): biasanya aman dihapus.</summary>
    BrowserCache,

    /// <summary>Dependencies/packages dev: node_modules, .gradle, venv, dll.</summary>
    DevDependencies,

    /// <summary>File update Windows: SoftwareDistribution, WinSxS, dll.</summary>
    WindowsUpdate,

    /// <summary>Installer/aplikasi: .exe/.msi di Downloads, Temp.</summary>
    AppInstaller,

    /// <summary>File sistem inti: pagefile, hiberfil, System Volume Information.</summary>
    System,

    /// <summary>File log aplikasi.</summary>
    Logs,

    /// <summary>File media: video/gambar/audio (umumnya di drive D).</summary>
    Media,

    /// <summary>File game/asset game (umumnya di drive E).</summary>
    Game,

    /// <summary>File sinkronisasi cloud (OneDrive) — bisa misleading saat sync.</summary>
    CloudSync,

    /// <summary>Tidak teridentifikasi — inilah "file misterius" yang user cari.</summary>
    Uncategorized
}
