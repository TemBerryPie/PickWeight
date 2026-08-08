using System.Text;

namespace PickWeight.Core.Services;

/// <summary>
/// 校验班级名称是否可以作为文件夹名使用。
/// </summary>
public static class ClassroomNameValidator
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public const int MaxLength = 50;

    /// <summary>
    /// 校验班级名是否有效。返回 null 表示有效,否则返回错误原因。
    /// </summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "班级名不能为空。";
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxLength)
        {
            return $"班级名不能超过 {MaxLength} 个字符。";
        }

        if (trimmed.IndexOfAny(InvalidFileNameChars) >= 0)
        {
            return "班级名不能包含以下字符: \\ / : * ? \" < > |";
        }

        // Windows 不允许文件名以空格或句点结尾
        if (trimmed.EndsWith(' ') || trimmed.EndsWith('.'))
        {
            return "班级名不能以空格或句点结尾。";
        }

        // 检查 Windows 保留名
        var baseName = trimmed;
        var dotIndex = trimmed.IndexOf('.');
        if (dotIndex > 0)
        {
            baseName = trimmed[..dotIndex];
        }

        if (WindowsReservedNames.Contains(baseName))
        {
            return $"“{baseName}”是 Windows 保留名称，不能用作班级名。";
        }

        return null;
    }

    /// <summary>
    /// 规范化班级名:去除两端空格、统一全角半角括号,用于重名检测。
    /// </summary>
    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name.Trim();

        // 全角括号转半角,避免"1班"与"1班"被视为不同名称
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            sb.Append(c switch
            {
                '(' => '(',
                ')' => ')',
                '【' => '[',
                '】' => ']',
                _ => c
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// 检查新名称在已有班级列表中是否重名(不区分大小写,全角半角归一化后比较)。
    /// </summary>
    public static bool IsDuplicate(string newName, IEnumerable<string> existingNames, string? excludeName = null)
    {
        var normalized = Normalize(newName);
        var excludeNormalized = excludeName is not null ? Normalize(excludeName) : null;

        foreach (var existing in existingNames)
        {
            var existingNormalized = Normalize(existing);

            // 重命名时排除自己
            if (excludeNormalized is not null &&
                string.Equals(existingNormalized, excludeNormalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(normalized, existingNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 校验班级名是否与已有列表重名。返回 null 表示不重名，否则返回错误原因。
    /// </summary>
    public static string? ValidateUnique(string name, IEnumerable<string> existingNames)
    {
        if (IsDuplicate(name, existingNames))
        {
            return $"班级“{Normalize(name)}”已存在，请换一个名称。";
        }

        return null;
    }
}
