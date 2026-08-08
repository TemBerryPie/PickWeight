using Windows.System;

namespace PickWeight_App.Models;

/// <summary>
/// 快捷键定义：一组修饰键（Ctrl/Alt/Shift/Win）加一个主键（字母/数字/F1-F12）。
/// <see cref="ToString"/> 输出「Ctrl+Alt+P」格式，与设置中持久化的字符串一致。
/// </summary>
public sealed record HotkeyDefinition(IReadOnlyList<HotkeyModifier> Modifiers, HotkeyKey Key)
{
    /// <summary>格式化快捷键文本，例如「Ctrl + Alt + P」。</summary>
    public string ToDisplayText() =>
        string.Join(" + ", Modifiers.Select(modifier => modifier.ToString()).Concat([Key.ToString()]));

    /// <summary>持久化格式：不带空格，例如「Ctrl+Alt+P」，兼容 <see cref="Services.GlobalHotkeyService.TryParse"/>。</summary>
    public override string ToString() =>
        string.Join("+", Modifiers.Select(modifier => modifier.ToString()).Concat([Key.ToString()]));

    /// <summary>是否是一个有效的组合：至少一个修饰键，且主键为字母/数字/F1-F12。</summary>
    public bool IsValid => Modifiers.Count > 0 && Key.IsSupported;

    /// <summary>
    /// 解析持久化字符串（如「Ctrl+Alt+P」「Shift+F1」）。
    /// 兼容旧格式：无空格；修饰键名不区分大小写；无法解析时返回 null。
    /// </summary>
    public static HotkeyDefinition? TryParse(string? value)
    {
        var parts = (value ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var modifiers = new List<HotkeyModifier>();
        var keyParts = new List<string>();
        foreach (var part in parts)
        {
            // 纯数字字符串（如「1」「0」）永远按主键处理：
            // Enum.TryParse 会把 "1" 误解析成数值为 1 的 Alt
            var isNumeric = part.Length > 0 && part.All(char.IsDigit);
            if (!isNumeric
                && Enum.TryParse<HotkeyModifier>(part, ignoreCase: true, out var modifier)
                && Enum.IsDefined(modifier))
            {
                // 同一修饰键重复出现视为无效输入
                if (modifiers.Contains(modifier))
                {
                    return null;
                }

                modifiers.Add(modifier);
                continue;
            }

            keyParts.Add(part);
        }

        // 只接受一个主键段；没有主键或主键不支持时按无效处理
        if (keyParts.Count != 1)
        {
            return null;
        }

        var key = HotkeyKey.Parse(keyParts[0]);
        return new HotkeyDefinition(modifiers, key);
    }
}

/// <summary>快捷键修饰键。</summary>
public enum HotkeyModifier
{
    Ctrl,
    Alt,
    Shift,
    Win
}

/// <summary>
/// 快捷键主键。仅支持 A-Z、0-9、F1-F12；非法按键用 <see cref="None"/> 表示。
/// </summary>
public readonly record struct HotkeyKey(VirtualKey WindowsVirtualKey)
{
    /// <summary>无效按键。</summary>
    public static HotkeyKey None { get; } = new(VirtualKey.None);

    public VirtualKey VirtualKey => WindowsVirtualKey;

    public bool IsSupported => IsLetter || IsDigit || IsFunctionKey;

    public bool IsModifier => WindowsVirtualKey is VirtualKey.Control
        or VirtualKey.Menu
        or VirtualKey.Shift
        or VirtualKey.LeftWindows
        or VirtualKey.RightWindows;

    public bool IsLetter => WindowsVirtualKey is >= VirtualKey.A and <= VirtualKey.Z;

    public bool IsDigit => WindowsVirtualKey is >= VirtualKey.Number0 and <= VirtualKey.Number9;

    public bool IsFunctionKey => WindowsVirtualKey is >= VirtualKey.F1 and <= VirtualKey.F12;

    /// <summary>把 WinUI 按键转成主键；不支持的类型返回 None。</summary>
    public static HotkeyKey FromVirtualKey(VirtualKey virtualKey) => new(virtualKey);

    public override string ToString()
    {
        if (IsLetter || IsFunctionKey)
        {
            // F1-F12 的符号名本身就是「F1」等；字母取大写字面
            return IsLetter ? WindowsVirtualKey.ToString() : WindowsVirtualKey.ToString();
        }

        if (IsDigit)
        {
            return WindowsVirtualKey switch
            {
                VirtualKey.Number0 => "0",
                VirtualKey.Number1 => "1",
                VirtualKey.Number2 => "2",
                VirtualKey.Number3 => "3",
                VirtualKey.Number4 => "4",
                VirtualKey.Number5 => "5",
                VirtualKey.Number6 => "6",
                VirtualKey.Number7 => "7",
                VirtualKey.Number8 => "8",
                _ => "9"
            };
        }

        return "未设置";
    }

    /// <summary>把持久化字符串中的主键段（如「P」）解析成主键；不支持时返回 None。</summary>
    public static HotkeyKey Parse(string keyText)
    {
        var text = keyText.Trim();
        if (text.Length == 0)
        {
            return None;
        }

        if (text.Length == 1)
        {
            var upper = char.ToUpperInvariant(text[0]);
            if (upper is >= 'A' and <= 'Z')
            {
                return new HotkeyKey((VirtualKey)upper);
            }

            if (upper is >= '0' and <= '9')
            {
                return new HotkeyKey((VirtualKey)(VirtualKey.Number0 + (upper - '0')));
            }

            return None;
        }

        // F1-F12
        if (text.Length is >= 2 and <= 3 && (text[0] == 'F' || text[0] == 'f') && int.TryParse(text[1..], out var number) && number is >= 1 and <= 12)
        {
            return new HotkeyKey((VirtualKey)(VirtualKey.F1 + number - 1));
        }

        return None;
    }
}
