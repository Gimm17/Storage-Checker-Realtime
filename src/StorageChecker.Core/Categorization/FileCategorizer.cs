namespace StorageChecker.Core.Categorization;

/// <summary>
/// Mengklasifikasi path file ke FileCategory berdasarkan CategoryRules.
/// Stateless & thread-safe — bisa dipakai bersama oleh banyak volume reader.
/// </summary>
public sealed class FileCategorizer
{
    private readonly IReadOnlyList<CategoryRule> _rules;

    public FileCategorizer(IReadOnlyList<CategoryRule>? rules = null)
    {
        _rules = rules ?? CategoryRules.Default;
    }

    /// <summary>
    /// Tentukan kategori dari path lengkap. Aturan dicek berurutan prioritas;
    /// yang pertama cocok menang. Jika tidak ada → Uncategorized.
    /// </summary>
    public FileCategory Categorize(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return FileCategory.Uncategorized;

        foreach (var rule in _rules)
        {
            if (rule.Matches(fullPath))
                return rule.Category;
        }

        return FileCategory.Uncategorized;
    }
}
