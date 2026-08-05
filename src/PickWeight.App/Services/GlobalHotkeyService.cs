using System.Runtime.InteropServices;
using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

/// <summary>Registers a deliberately narrow, configurable system-wide quick-draw shortcut.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApplySettings = 0x8001;
    private const int HotkeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private readonly ISettingsService _settings;
    private readonly QuickDrawWindowService _quickDraw;
    private readonly AppLogService _log;
    private readonly ManualResetEventSlim _messageLoopReady = new();
    private readonly object _gate = new();
    private Thread? _messageThread;
    private uint _threadId;
    private bool _disposed;

    public GlobalHotkeyService(ISettingsService settings, QuickDrawWindowService quickDraw, AppLogService log)
    {
        _settings = settings;
        _quickDraw = quickDraw;
        _log = log;
        _settings.Changed += Settings_Changed;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_messageThread is not null || _disposed) return;
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "PickWeight Global Hotkey"
            };
            _messageThread.Start();
        }

        _messageLoopReady.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _disposed = true;
        _settings.Changed -= Settings_Changed;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012, 0, 0);
        _messageLoopReady.Dispose();
    }

    private void Settings_Changed(object? sender, PickWeight.Core.Models.SettingsChangedEventArgs args)
    {
        if (args.GlobalHotkeyChanged && _threadId != 0)
        {
            PostThreadMessage(_threadId, WmApplySettings, 0, 0);
        }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0);
        RegisterCurrentShortcut();
        _messageLoopReady.Set();

        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == WmHotkey)
            {
                App.DispatcherQueue.TryEnqueue(async () => await _quickDraw.RequestQuickDrawAsync());
            }
            else if (message.Message == WmApplySettings)
            {
                RegisterCurrentShortcut();
            }
        }

        UnregisterHotKey(0, HotkeyId);
    }

    private void RegisterCurrentShortcut()
    {
        UnregisterHotKey(0, HotkeyId);
        if (!TryParse(_settings.Current.GlobalHotkey, out var virtualKey))
        {
            _ = _log.InfoAsync("Global shortcut ignored because its format is invalid.");
            return;
        }

        if (!RegisterHotKey(0, HotkeyId, ModControl | ModAlt | ModNoRepeat, virtualKey))
        {
            _ = _log.InfoAsync("Global shortcut could not be registered because it is in use.");
        }
    }

    internal static bool TryParse(string? value, out uint virtualKey)
    {
        virtualKey = 0;
        var parts = (value ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !parts.Contains("Ctrl", StringComparer.OrdinalIgnoreCase) || !parts.Contains("Alt", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = parts.SingleOrDefault(part => !part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) && !part.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        if (key is { Length: 1 } && char.IsAsciiLetter(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint windowHandle, uint minimumFilter, uint maximumFilter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint windowHandle, uint minimumFilter, uint maximumFilter, uint removeMessage);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }
}
