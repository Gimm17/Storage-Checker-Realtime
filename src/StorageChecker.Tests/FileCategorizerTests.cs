using StorageChecker.Core.Categorization;

namespace StorageChecker.Tests;

public class FileCategorizerTests
{
    private readonly FileCategorizer _sut = new();

    [Theory]
    [InlineData(@"C:\Users\HP\AppData\Local\Google\Chrome\User Data\Default\Cache\f_001", FileCategory.BrowserCache)]
    [InlineData(@"C:\Users\HP\AppData\Local\Mozilla\Firefox\Profiles\abc\cache2\entries\x", FileCategory.BrowserCache)]
    [InlineData(@"C:\proj\app\node_modules\react\index.js", FileCategory.DevDependencies)]
    [InlineData(@"C:\proj\rust\target\release\app.exe", FileCategory.DevDependencies)]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download\abc.cab", FileCategory.WindowsUpdate)]
    [InlineData(@"C:\Windows\WinSxS\amd64_something\file.dll", FileCategory.WindowsUpdate)]
    [InlineData(@"C:\pagefile.sys", FileCategory.System)]
    [InlineData(@"D:\System Volume Information\tracking.log", FileCategory.System)]
    [InlineData(@"C:\Users\HP\Downloads\setup.exe", FileCategory.AppInstaller)]
    [InlineData(@"C:\Users\HP\OneDrive\Documents\notes.txt", FileCategory.CloudSync)]
    [InlineData(@"D:\Movies\film.mkv", FileCategory.Media)]
    [InlineData(@"E:\SteamApps\common\game\data.pak", FileCategory.Game)]
    [InlineData(@"C:\app\logs\server.log", FileCategory.Logs)]
    [InlineData(@"D:\misc\unknown_blob.dat", FileCategory.Uncategorized)]
    public void Categorize_returns_expected(string path, FileCategory expected)
    {
        Assert.Equal(expected, _sut.Categorize(path));
    }

    [Fact]
    public void Empty_path_is_uncategorized()
    {
        Assert.Equal(FileCategory.Uncategorized, _sut.Categorize(""));
    }
}
