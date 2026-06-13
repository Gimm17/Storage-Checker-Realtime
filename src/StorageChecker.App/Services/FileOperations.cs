using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace StorageChecker.App.Services;

/// <summary>
/// Operasi file dari UI: buka di Explorer & hapus (default ke Recycle Bin).
/// Quoting argumen aman untuk mencegah command injection lewat path.
/// </summary>
public sealed class FileOperations
{
    /// <summary>Buka Explorer dengan file ter-highlight.</summary>
    public bool OpenInExplorer(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return false;

            // /select butuh path dalam tanda kutip; ProcessStartInfo.ArgumentList
            // meng-escape otomatis sehingga aman terhadap spasi/karakter khusus.
            var psi = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            psi.ArgumentList.Add("/select,");
            psi.ArgumentList.Add(fullPath);
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hapus file. Default ke Recycle Bin (bisa di-undo); permanen jika diminta.
    /// Mengembalikan (sukses, pesanError).
    /// </summary>
    public (bool ok, string? error) Delete(string fullPath, bool permanent = false)
    {
        try
        {
            if (!File.Exists(fullPath))
                return (false, "File tidak ditemukan (mungkin sudah terhapus).");

            FileSystem.DeleteFile(
                fullPath,
                UIOption.OnlyErrorDialogs,
                permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin);

            return (true, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Akses ditolak — file sistem atau sedang dipakai proses lain.");
        }
        catch (IOException ex)
        {
            return (false, $"File terkunci / sedang digunakan: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
