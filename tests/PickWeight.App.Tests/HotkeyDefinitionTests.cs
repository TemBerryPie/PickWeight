using System.Reflection;
using PickWeight_App.Models;
using PickWeight_App.Services;

namespace PickWeight.App.Tests;

/// <summary>快捷键定义解析与全局快捷键注册的兼容性测试。</summary>
public sealed class HotkeyDefinitionTests
{
    // GlobalHotkeyService.TryParse 是 internal，通过反射访问
    private static readonly MethodInfo TryParseMethod = typeof(GlobalHotkeyService)
        .GetMethod("TryParse", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static bool TryParseGlobalHotkey(string text, out uint virtualKey)
    {
        var arguments = new object?[] { text, null };
        var result = (bool)TryParseMethod.Invoke(null, arguments)!;
        virtualKey = (uint)(arguments[1] ?? 0u);
        return result;
    }
    [Fact]
    public void ToString_UsesPersistedFormat()
    {
        var definition = new HotkeyDefinition([HotkeyModifier.Ctrl, HotkeyModifier.Alt], new HotkeyKey(Windows.System.VirtualKey.P));
        Assert.Equal("Ctrl+Alt+P", definition.ToString());
    }

    [Fact]
    public void ToDisplayText_AddsSpaces()
    {
        var definition = new HotkeyDefinition([HotkeyModifier.Ctrl, HotkeyModifier.Alt], new HotkeyKey(Windows.System.VirtualKey.P));
        Assert.Equal("Ctrl + Alt + P", definition.ToDisplayText());
    }

    [Theory]
    [InlineData("Ctrl+Alt+P", 0x50)]
    [InlineData("ctrl+alt+p", 0x50)]
    [InlineData("Alt+Ctrl+K", 0x4B)]
    [InlineData("Ctrl+Alt+Shift+F5", 0x74)]
    [InlineData("Ctrl+Alt+1", 0x31)]
    [InlineData("Ctrl+Alt+0", 0x30)]
    public void TryParse_AcceptsSupportedCombinations(string text, uint expectedVirtualKey)
    {
        Assert.True(TryParseGlobalHotkey(text, out var virtualKey), $"应能解析 {text}");
        Assert.Equal(expectedVirtualKey, virtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("Ctrl+P")]
    [InlineData("Ctrl+Alt+;")]
    [InlineData("Alt+P")]
    [InlineData("Ctrl+Alt+P+Q")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Ctrl+Alt+P")]
    public void TryParse_RejectsUnsupportedCombinations(string text)
    {
        Assert.False(TryParseGlobalHotkey(text, out _), $"不应能解析 {text}");
    }

    [Fact]
    public void HotkeyKey_Parse_DigitsAndFunctionKeys()
    {
        Assert.Equal(Windows.System.VirtualKey.Number3, HotkeyKey.Parse("3").VirtualKey);
        Assert.Equal(Windows.System.VirtualKey.F12, HotkeyKey.Parse("F12").VirtualKey);
        Assert.False(HotkeyKey.Parse("f13").IsSupported);
        Assert.False(HotkeyKey.Parse("Esc").IsSupported);
    }
}
