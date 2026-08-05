using System.Text;

namespace PickWeight_App.Services;

/// <summary>Writes the latest committed draw as plain text for OBS text-source overlays.</summary>
public sealed class ObsOverlayService
{
    public string OutputPath { get; }

    public ObsOverlayService(string? outputPath = null) =>
        OutputPath = outputPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickWeight", "overlay", "latest-draw.txt");

    public async Task WriteLatestDrawAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(OutputPath)!;
        Directory.CreateDirectory(directory);
        var temporary = OutputPath + ".tmp";
        await File.WriteAllTextAsync(temporary, string.Join(Environment.NewLine, names), new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, OutputPath, true);
    }
}
