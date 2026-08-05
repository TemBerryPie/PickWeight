using Microsoft.Win32;
using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PickWeight";
    private readonly ISettingsService _settings;

    public StartupService(ISettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, args) =>
        {
            if (args.StartWithWindowsChanged)
            {
                Apply();
            }
        };
    }

    public void Apply()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (_settings.Current.StartWithWindows)
            key.SetValue(ValueName, $"\"{GetLaunchPath()}\"");
        else
            key.DeleteValue(ValueName, false);
    }

    private static string GetLaunchPath()
    {
        var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDirectory = Directory.GetParent(appDirectory)?.FullName;
        var launcherPath = parentDirectory is null ? null : Path.Combine(parentDirectory, "PickWeight.exe");
        return !string.IsNullOrWhiteSpace(launcherPath) && File.Exists(launcherPath)
            ? launcherPath
            : Environment.ProcessPath ?? Path.Combine(appDirectory, "PickWeight.App.exe");
    }
}
