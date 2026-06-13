using System.Text.RegularExpressions;

namespace StorageChecker.Core.Categorization;

/// <summary>
/// Satu aturan kategorisasi: pola path (substring/regex) → kategori, dengan prioritas.
/// Prioritas lebih kecil = dicek lebih dulu (lebih spesifik menang).
/// </summary>
public sealed record CategoryRule(
    FileCategory Category,
    int Priority,
    Func<string, bool> Matches,
    string Description);

/// <summary>
/// Daftar aturan default. Match terhadap path lengkap (lowercase).
/// Dipisah dari FileCategorizer agar mudah ditambah/diuji.
/// </summary>
public static class CategoryRules
{
    private static bool Contains(string path, string token) =>
        path.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithAny(string path, params string[] exts) =>
        exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Aturan terurut berdasarkan prioritas (kecil dulu).</summary>
    public static IReadOnlyList<CategoryRule> Default { get; } = Build();

    private static List<CategoryRule> Build()
    {
        var rules = new List<CategoryRule>
        {
            // ── System (paling kritis, cek paling awal) ──
            new(FileCategory.System, 10,
                p => EndsWithAny(p, "pagefile.sys", "hiberfil.sys", "swapfile.sys")
                     || Contains(p, @"\system volume information\")
                     || Contains(p, @"\$recycle.bin\"),
                "File sistem inti / pagefile"),

            // ── Windows Update ──
            new(FileCategory.WindowsUpdate, 20,
                p => Contains(p, @"\windows\softwaredistribution\")
                     || Contains(p, @"\windows\winsxs\")
                     || Contains(p, @"\$winreagent\")
                     || Contains(p, @"\windows\installer\"),
                "File update Windows"),

            // ── Cloud Sync (OneDrive) ──
            new(FileCategory.CloudSync, 30,
                p => Contains(p, @"\onedrive\") || Contains(p, @"\onedrive -"),
                "Sinkronisasi OneDrive"),

            // ── Browser Cache ──
            new(FileCategory.BrowserCache, 40,
                p => Contains(p, @"\cache\") || Contains(p, @"\code cache\")
                     || Contains(p, @"\gpucache\") || Contains(p, @"\cache2\")
                     || Contains(p, @"\user data\") && Contains(p, "cache")
                     || Contains(p, @"\inetcache\"),
                "Cache browser"),

            // ── Dev Dependencies ──
            new(FileCategory.DevDependencies, 50,
                p => Contains(p, @"\node_modules\") || Contains(p, @"\.gradle\")
                     || Contains(p, @"\.m2\") || Contains(p, @"\__pycache__\")
                     || Contains(p, @"\venv\") || Contains(p, @"\.venv\")
                     || Contains(p, @"\.nuget\packages\") || Contains(p, @"\vendor\")
                     || Contains(p, @"\.cargo\") || Contains(p, @"\target\debug\")
                     || Contains(p, @"\target\release\") || Contains(p, @"\dist\")
                     || Contains(p, @"\.next\") || Contains(p, @"\.gradle\"),
                "Dependencies / packages dev"),

            // ── Logs ──
            new(FileCategory.Logs, 60,
                p => EndsWithAny(p, ".log") || Contains(p, @"\logs\"),
                "File log"),

            // ── App / Installer ──
            new(FileCategory.AppInstaller, 70,
                p => (Contains(p, @"\downloads\") && EndsWithAny(p, ".exe", ".msi", ".zip", ".7z", ".rar"))
                     || Contains(p, @"\appdata\local\temp\")
                     || Contains(p, @"\windows\temp\")
                     || EndsWithAny(p, ".msi", ".msix"),
                "Installer / file aplikasi"),

            // ── Media ──
            new(FileCategory.Media, 80,
                p => EndsWithAny(p, ".mp4", ".mkv", ".avi", ".mov", ".png", ".jpg",
                                    ".jpeg", ".gif", ".psd", ".mp3", ".wav", ".flac"),
                "File media"),

            // ── Game ──
            new(FileCategory.Game, 90,
                p => EndsWithAny(p, ".pak", ".vpk", ".bsa", ".asset", ".uasset")
                     || Contains(p, @"\steamapps\") || Contains(p, @"\games\"),
                "File / asset game"),
        };

        return rules.OrderBy(r => r.Priority).ToList();
    }
}
