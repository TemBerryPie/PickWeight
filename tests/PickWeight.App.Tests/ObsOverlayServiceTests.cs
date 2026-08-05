using PickWeight_App.Services;

namespace PickWeight.App.Tests;

public sealed class ObsOverlayServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PickWeight.Obs.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteLatestDrawAsync_AtomicallyReplacesOverlayText()
    {
        var outputPath = Path.Combine(_directory, "overlay", "latest-draw.txt");
        var service = new ObsOverlayService(outputPath);

        await service.WriteLatestDrawAsync(["张三", "李四"]);

        Assert.Equal($"张三{Environment.NewLine}李四", await File.ReadAllTextAsync(outputPath));
        Assert.False(File.Exists(outputPath + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
