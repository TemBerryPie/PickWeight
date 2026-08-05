using System.Text;

namespace PickWeight_App.Services;

/// <summary>Small local diagnostic log that intentionally records no member names or roster data.</summary>
public sealed class AppLogService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickWeight", "logs", "pickweight.log");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task InfoAsync(string message, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.AppendAllTextAsync(_path, $"{DateTimeOffset.Now:O} INFO {message}{Environment.NewLine}", Encoding.UTF8, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public Task<string> ReadAsync(CancellationToken cancellationToken = default) =>
        File.Exists(_path) ? File.ReadAllTextAsync(_path, cancellationToken) : Task.FromResult(string.Empty);
}
