using StorageChecker.Core.Categorization;
using StorageChecker.Core.Safety;

namespace StorageChecker.Tests;

public class DeletionSafetyClassifierTests
{
    private readonly DeletionSafetyClassifier _sut = new();

    [Theory]
    [InlineData(@"C:\Windows\System32\kernel32.dll", false, FileCategory.Uncategorized, SafetyLevel.Dangerous)]
    [InlineData(@"C:\pagefile.sys", false, FileCategory.System, SafetyLevel.Dangerous)]
    [InlineData(@"C:\Program Files\App\app.exe", false, FileCategory.Uncategorized, SafetyLevel.Dangerous)]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download\x.cab", false, FileCategory.WindowsUpdate, SafetyLevel.Dangerous)]
    [InlineData(@"C:\anything.dat", true, FileCategory.Uncategorized, SafetyLevel.Dangerous)]
    public void Dangerous_paths_flagged(string path, bool sys, FileCategory cat, SafetyLevel expected)
    {
        Assert.Equal(expected, _sut.Classify(path, sys, cat));
    }

    [Theory]
    [InlineData(@"C:\Users\HP\AppData\Local\Chrome\Cache\f_01", false, FileCategory.BrowserCache, SafetyLevel.Safe)]
    [InlineData(@"C:\app\logs\server.log", false, FileCategory.Logs, SafetyLevel.Safe)]
    [InlineData(@"C:\Users\HP\Downloads\setup.exe", false, FileCategory.AppInstaller, SafetyLevel.Safe)]
    public void Safe_files_flagged(string path, bool sys, FileCategory cat, SafetyLevel expected)
    {
        Assert.Equal(expected, _sut.Classify(path, sys, cat));
    }

    [Theory]
    [InlineData(@"C:\proj\node_modules\react\index.js", false, FileCategory.DevDependencies, SafetyLevel.Caution)]
    [InlineData(@"C:\Users\HP\OneDrive\notes.txt", false, FileCategory.CloudSync, SafetyLevel.Caution)]
    [InlineData(@"D:\misc\unknown.dat", false, FileCategory.Uncategorized, SafetyLevel.Caution)]
    public void Caution_files_flagged(string path, bool sys, FileCategory cat, SafetyLevel expected)
    {
        Assert.Equal(expected, _sut.Classify(path, sys, cat));
    }
}
