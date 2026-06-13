using StorageChecker.Core.Aggregation;
using StorageChecker.Core.Categorization;
using StorageChecker.Core.Storage;

namespace StorageChecker.Tests;

public class EventAggregatorTests
{
    private static FileEvent Ev(string path, long delta) => new()
    {
        TimestampUtc = DateTime.UtcNow,
        Drive = 'C',
        FullPath = path,
        FileName = Path.GetFileName(path),
        DeltaBytes = delta,
        SizeBytes = delta,
        Category = FileCategory.DevDependencies
    };

    [Fact]
    public void Large_files_kept_individual()
    {
        var agg = new EventAggregator(individualThresholdBytes: 5_000_000);
        var batch = new List<FileEvent> { Ev(@"C:\big\file.bin", 10_000_000) };

        var result = agg.Aggregate(batch);

        Assert.Single(result);
        Assert.Equal(@"C:\big\file.bin", result[0].FullPath);
    }

    [Fact]
    public void Small_files_grouped_by_folder()
    {
        var agg = new EventAggregator(individualThresholdBytes: 5_000_000);
        var batch = new List<FileEvent>
        {
            Ev(@"C:\proj\node_modules\a.js", 1000),
            Ev(@"C:\proj\node_modules\b.js", 2000),
            Ev(@"C:\proj\node_modules\c.js", 3000),
        };

        var result = agg.Aggregate(batch);

        Assert.Single(result);
        Assert.Equal(6000, result[0].DeltaBytes);
        Assert.Equal(@"C:\proj\node_modules", result[0].FullPath);
    }

    [Fact]
    public void Sorted_by_delta_descending()
    {
        var agg = new EventAggregator(individualThresholdBytes: 100);
        var batch = new List<FileEvent>
        {
            Ev(@"C:\a.bin", 500),
            Ev(@"C:\b.bin", 9000),
            Ev(@"C:\c.bin", 3000),
        };

        var result = agg.Aggregate(batch);

        Assert.Equal(9000, result[0].DeltaBytes);
        Assert.Equal(3000, result[1].DeltaBytes);
        Assert.Equal(500, result[2].DeltaBytes);
    }
}
