using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PickWeight.Launcher;

internal static class Program
{
    private const uint MbIconError = 0x00000010;

    [STAThread]
    private static void Main(string[] args)
    {
        var applicationDirectory = AppContext.BaseDirectory;
        var targetPath = Path.Combine(applicationDirectory, "Data", "PickWeight.App.exe");
        if (!File.Exists(targetPath))
        {
            ShowError($"找不到程序文件：\n{targetPath}\n\n请确认 Data 文件夹完整存在。");
            Environment.ExitCode = 2;
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetPath,
                WorkingDirectory = Path.GetDirectoryName(targetPath)!,
                UseShellExecute = false
            };
            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            ShowError($"无法启动 PickWeight。\n\n{exception.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void ShowError(string message) =>
        MessageBox(0, message, "PickWeight", MbIconError);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint windowHandle, string text, string caption, uint type);
}
