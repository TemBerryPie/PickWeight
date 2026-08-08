using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PickWeight_App.Models;
using Windows.System;

namespace PickWeight_App.Controls;

/// <summary>
/// 快捷键录制控件：点击聚焦后捕获按键组合，支持修饰键（Ctrl/Alt/Shift/Win）
/// 加 A-Z、0-9、F1-F12；再次点击重新录制，录制中按 Esc 取消。
/// 输出格式为「Ctrl+Alt+P」（见 <see cref="HotkeyDefinition.ToString"/>）。
/// </summary>
public sealed partial class HotkeyRecorderControl : UserControl
{
    private bool _isRecording;

    public HotkeyRecorderControl()
    {
        InitializeComponent();
        DisplayText = "点击后按组合键…";
    }

    /// <summary>控件标题文本。</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(HotkeyRecorderControl),
        new PropertyMetadata(string.Empty));

    /// <summary>当前快捷键；null 表示未设置。</summary>
    public HotkeyDefinition? Hotkey
    {
        get => (HotkeyDefinition?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(HotkeyDefinition),
        typeof(HotkeyRecorderControl),
        new PropertyMetadata(null, OnHotkeyChanged));

    /// <summary>按钮上显示的快捷键文本。</summary>
    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText),
        typeof(string),
        typeof(HotkeyRecorderControl),
        new PropertyMetadata(string.Empty));

    /// <summary>用户录制出新的有效快捷键时触发。</summary>
    public event EventHandler<HotkeyRecordedEventArgs>? Recorded;

    private static void OnHotkeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not HotkeyRecorderControl control || control._isRecording)
        {
            return;
        }

        control.DisplayText = control.Hotkey is { } hotkey
            ? hotkey.ToDisplayText()
            : "点击后按组合键…";
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            CancelRecording();
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        _isRecording = true;
        DisplayText = "按下组合键（Esc 取消）…";
        CaptureButton.Focus(FocusState.Programmatic);
    }

    private void CancelRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        UpdateDisplayFromHotkey();
    }

    private void UpdateDisplayFromHotkey()
    {
        DisplayText = Hotkey is { } hotkey ? hotkey.ToDisplayText() : "点击后按组合键…";
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        if (!_isRecording)
        {
            base.OnKeyDown(args);
            return;
        }

        // Esc 取消录制，不产生快捷键
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            CancelRecording();
            return;
        }

        var modifiers = new List<HotkeyModifier>();
        if (IsModifierDown(Windows.System.VirtualKeyModifiers.Control)) modifiers.Add(HotkeyModifier.Ctrl);
        if (IsModifierDown(Windows.System.VirtualKeyModifiers.Menu)) modifiers.Add(HotkeyModifier.Alt);
        if (IsModifierDown(Windows.System.VirtualKeyModifiers.Shift)) modifiers.Add(HotkeyModifier.Shift);
        if (IsModifierDown(Windows.System.VirtualKeyModifiers.Windows)) modifiers.Add(HotkeyModifier.Win);

        // 修饰键单独按下不算一个完整组合，继续等待主键
        var key = HotkeyKey.FromVirtualKey(args.Key);
        if (key.IsModifier || !key.IsSupported || modifiers.Count == 0)
        {
            return;
        }

        args.Handled = true;
        var definition = new HotkeyDefinition(modifiers, key);
        _isRecording = false;
        Hotkey = definition;
        DisplayText = definition.ToDisplayText();
        Recorded?.Invoke(this, new HotkeyRecordedEventArgs(definition));
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _isRecording = false;
        Hotkey = null;
        DisplayText = "点击后按组合键…";
        Recorded?.Invoke(this, new HotkeyRecordedEventArgs(null));
    }

    private static bool IsModifierDown(Windows.System.VirtualKeyModifiers modifier) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(MapToVirtualKey(modifier))
            & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

    private static VirtualKey MapToVirtualKey(Windows.System.VirtualKeyModifiers modifier) => modifier switch
    {
        Windows.System.VirtualKeyModifiers.Control => VirtualKey.Control,
        Windows.System.VirtualKeyModifiers.Menu => VirtualKey.Menu,
        Windows.System.VirtualKeyModifiers.Shift => VirtualKey.Shift,
        Windows.System.VirtualKeyModifiers.Windows => VirtualKey.LeftWindows,
        _ => VirtualKey.None
    };
}

/// <summary>录制完成事件参数；Hotkey 为 null 表示用户清除了快捷键。</summary>
public sealed class HotkeyRecordedEventArgs(HotkeyDefinition? hotkey) : EventArgs
{
    public HotkeyDefinition? Hotkey { get; } = hotkey;
}
