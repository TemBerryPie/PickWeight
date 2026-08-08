using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Controls;

namespace PickWeight.App.Tests;

/// <summary>
/// U9 工程兜底：WinUI 的 <c>Icon="..."</c> 字符串在 XAML 编译期不校验。
/// 这里直接加载应用实际绑定到的 WinUI Symbol 枚举，逐个验证项目 XAML 里的
/// 图标名，避免坏 Symbol 只在运行期以 XamlParseException 暴露。
/// </summary>
public sealed class XamlSymbolSanityTests
{
    private static readonly Regex IconAttributeRegex = new(
        @"\bIcon\s*=\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void EveryXamlIcon_IsAValidSymbolEnumValue()
    {
        // typeof(Symbol) resolves to the WinUI projection the app compiles
        // against (Microsoft.UI.Xaml family), so this stays in sync with the
        // runtime Symbol surface rather than a hard-coded copy of the enum.
        var symbolType = typeof(Symbol);
        var symbolNames = Enum.GetNames(symbolType);
        var xamlFiles = FindProjectXamlFiles();

        Assert.NotEmpty(xamlFiles);

        var invalid = new List<string>();
        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in IconAttributeRegex.Matches(content))
            {
                var icon = match.Groups[1].Value;
                if (!symbolNames.Contains(icon, StringComparer.OrdinalIgnoreCase))
                {
                    invalid.Add($"{Path.GetFileName(file)}: Icon=\"{icon}\"");
                }
            }
        }

        Assert.True(
            invalid.Count == 0,
            $"以下 XAML Icon 值不是有效的 Symbol 枚举值:{Environment.NewLine}{string.Join(Environment.NewLine, invalid)}");
    }

    private static IReadOnlyList<string> FindProjectXamlFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, "src", "PickWeight.App")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var appRoot = Path.Combine(directory!.FullName, "src", "PickWeight.App");
        return Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("dist", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
