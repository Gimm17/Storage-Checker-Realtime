using StorageChecker.Core.Storage;

namespace StorageChecker.Core.Aggregation;

/// <summary>
/// Mengurangi noise sebelum event sampai ke UI (mode "smart filtered"):
/// - event kecil dalam folder yang sama digabung jadi 1 baris ringkas per folder
/// - hanya file dengan delta >= threshold yang ditampilkan sebagai baris individual
/// Catatan: SEMUA event tetap disimpan ke DB di layer lain; ini hanya untuk feed UI.
/// </summary>
public sealed class EventAggregator
{
    private readonly long _individualThresholdBytes;
    private readonly Dictionary<string, FileEvent> _folderGroups = new(StringComparer.OrdinalIgnoreCase);

    public EventAggregator(long individualThresholdBytes = 5L * 1024 * 1024)
    {
        _individualThresholdBytes = individualThresholdBytes;
    }

    /// <summary>
    /// Proses satu batch event mentah → kembalikan daftar baris siap-tampil
    /// (campuran file besar individual + ringkasan folder).
    /// </summary>
    public IReadOnlyList<FileEvent> Aggregate(IReadOnlyList<FileEvent> batch)
    {
        var individual = new List<FileEvent>();
        _folderGroups.Clear();

        foreach (var e in batch)
        {
            if (e.DeltaBytes >= _individualThresholdBytes)
            {
                individual.Add(e);
                continue;
            }

            // File kecil → gabung ke ringkasan folder induk.
            var folder = GetFolder(e.FullPath);
            if (!_folderGroups.TryGetValue(folder, out var grp))
            {
                grp = new FileEvent
                {
                    TimestampUtc = e.TimestampUtc,
                    Drive = e.Drive,
                    FullPath = folder,
                    FileName = $"[{GetFolderLabel(folder)}] (grup)",
                    Category = e.Category,
                    Safety = e.Safety,
                    DeltaBytes = 0,
                    SizeBytes = 0
                };
                _folderGroups[folder] = grp;
            }
            grp.DeltaBytes += e.DeltaBytes;
            grp.SizeBytes += e.SizeBytes;
            grp.TimestampUtc = e.TimestampUtc; // paling baru
        }

        // Gabung & urut by pertambahan terbesar.
        var result = new List<FileEvent>(individual);
        result.AddRange(_folderGroups.Values);
        result.Sort((a, b) => b.DeltaBytes.CompareTo(a.DeltaBytes));
        return result;
    }

    private static string GetFolder(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        return string.IsNullOrEmpty(dir) ? fullPath : dir;
    }

    private static string GetFolderLabel(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? folder : name;
    }
}
