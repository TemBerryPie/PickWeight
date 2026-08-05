using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PickWeight.Core.Models;
using PickWeight_App.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;

namespace PickWeight_App;

/// <summary>
/// A compact, transient window used by the floating quick-draw button.  It has
/// no knowledge of draw rules; callers provide the candidates, chosen name and
/// the callbacks that persist or discard the prepared draw.
/// </summary>
public sealed partial class QuickDrawResultWindow : Window
{
    private const int CompactLogicalWidth = 440;
    private const int CompactLogicalHeight = 196;
    private const int PresentationLogicalWidth = 1280;
    private const int PresentationLogicalHeight = 720;
    private const int AnimationDurationMilliseconds = 1500;
    private const int AnimationIntervalMilliseconds = 75;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private readonly nint _windowHandle;
    private readonly bool _isPresentationMode;
    private readonly TransientAcrylicBackdrop _acrylicBackdrop;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationStopwatch = new();
    private readonly Random _random = new();
    private TaskCompletionSource? _closedCompletion;
    private IReadOnlyList<string> _candidateNames = [];
    private string _finalName = string.Empty;
    private Func<CancellationToken, Task>? _commitAsync;
    private Func<CancellationToken, Task>? _cancelAsync;
    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationRegistration;
    private readonly LowLevelMouseProc _mouseHookProc;
    private nint _mouseHook;
    private bool _isShown;
    private bool _animationCompleted;
    private bool _isCompletingAnimation;
    private bool _isClosed;
    private bool _cancelInvoked;

    public QuickDrawResultWindow(bool isPresentationMode = false)
    {
        InitializeComponent();

        _isPresentationMode = isPresentationMode;
        Title = isPresentationMode ? "投影抽取" : "快速抽取";
        _mouseHookProc = MouseHookCallback;
        _acrylicBackdrop = new TransientAcrylicBackdrop(this);
        AppWindow.SetIcon(App.IconPath);
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        ConfigureNativeWindow(_windowHandle);
        Activated += QuickDrawResultWindow_Activated;
        Closed += QuickDrawResultWindow_Closed;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationIntervalMilliseconds) };
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    public void ApplyAppearance(AppSettings settings)
    {
        WindowRoot.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _acrylicBackdrop.Apply(settings);
    }

    /// <summary>
    /// Shows the animated result.  The commit callback is invoked exactly once
    /// after the animation ends; closing before then invokes the cancel callback.
    /// </summary>
    public async Task ShowAsync(
        IReadOnlyList<string> candidateNames,
        string finalName,
        Func<CancellationToken, Task> commitAsync,
        Func<CancellationToken, Task> cancelAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalName);
        ArgumentNullException.ThrowIfNull(commitAsync);
        ArgumentNullException.ThrowIfNull(cancelAsync);

        if (_closedCompletion is not null)
        {
            throw new InvalidOperationException("此快速抽取窗口已显示。");
        }

        _candidateNames = candidateNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Append(finalName.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _finalName = finalName.Trim();
        _commitAsync = commitAsync;
        _cancelAsync = cancelAsync;
        _cancellationToken = cancellationToken;
        _closedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cancellationRegistration = cancellationToken.Register(CloseFromCancellation);

        NameText.FontSize = _isPresentationMode ? 128 : 44;
        NameText.Text = NextCandidateName();
        ResizeAndCenter();
        _isShown = true;
        AppWindow.Show();
        Activate();
        InstallOutsideClickHook();
        App.GetService<Services.AnimationService>().PlayPopupEntrance(WindowRoot);
        _animationStopwatch.Restart();
        _animationTimer.Start();

        await _closedCompletion.Task;
    }

    private void WindowRoot_Loaded(object sender, RoutedEventArgs e) => ResizeAndCenter();

    private void WindowRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void QuickDrawResultWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isShown && args.WindowActivationState == WindowActivationState.Deactivated && !_isClosed)
        {
            Close();
        }
    }

    private async void AnimationTimer_Tick(object? sender, object e)
    {
        if (_isClosed || _isCompletingAnimation)
        {
            return;
        }

        if (_animationStopwatch.ElapsedMilliseconds < AnimationDurationMilliseconds)
        {
            NameText.Text = NextCandidateName();
            return;
        }

        _isCompletingAnimation = true;
        _animationTimer.Stop();
        _animationCompleted = true;
        NameText.Text = _finalName;
        App.GetService<Services.AnimationService>().PlayResultReveal(NameText, 0);
        try
        {
            await _commitAsync!(_cancellationToken);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // The cancel callback below restores the prepared draw when a caller
            // cancels while the animation is completing.
            await InvokeCancelAsync();
        }
        catch
        {
            // Restore the prepared draw if persistence fails.  This keeps a
            // failed quick result from affecting a later draw in memory.
            await InvokeCancelAsync();
        }
        finally
        {
            _isCompletingAnimation = false;
        }
    }

    private string NextCandidateName()
    {
        if (_candidateNames.Count == 0)
        {
            return _finalName;
        }

        return _candidateNames[_random.Next(_candidateNames.Count)];
    }

    private void ResizeAndCenter()
    {
        var scale = WindowRoot.XamlRoot?.RasterizationScale
            ?? Math.Max(1d, GetDpiForWindow(_windowHandle) / 96d);
        var logicalWidth = _isPresentationMode ? PresentationLogicalWidth : CompactLogicalWidth;
        var logicalHeight = _isPresentationMode ? PresentationLogicalHeight : CompactLogicalHeight;
        var size = new SizeInt32(
            (int)Math.Ceiling(logicalWidth * scale),
            (int)Math.Ceiling(logicalHeight * scale));
        AppWindow.Resize(size);

        GetCursorPos(out var cursorPosition);
        var displayArea = DisplayArea.GetFromPoint(
            new PointInt32(cursorPosition.X, cursorPosition.Y),
            DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void CloseFromCancellation()
    {
        if (!App.DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed)
                {
                    Close();
                }
            }))
        {
            _closedCompletion?.TrySetCanceled(_cancellationToken);
        }
    }

    private async void QuickDrawResultWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        _isShown = false;
        RemoveOutsideClickHook();
        _animationTimer.Stop();
        _animationStopwatch.Reset();
        _cancellationRegistration.Dispose();
        if (!_animationCompleted)
        {
            await InvokeCancelAsync();
        }

        _closedCompletion?.TrySetResult();
    }

    private async Task InvokeCancelAsync()
    {
        if (_cancelInvoked)
        {
            return;
        }

        _cancelInvoked = true;
        try
        {
            // Discard must still persist when the caller cancelled the request;
            // passing an already-cancelled token could leave a prepared draw in
            // memory without restoring its snapshot.
            var cancellationToken = _cancellationToken.IsCancellationRequested
                ? CancellationToken.None
                : _cancellationToken;
            await _cancelAsync!(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected way to abandon a quick draw.
        }
        catch
        {
            // Closing the transient window should always remain responsive.
        }
    }

    private static void ConfigureNativeWindow(nint windowHandle)
    {
        var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        SetWindowLongPtr(windowHandle, GwlStyle, new nint(style));

        var extendedStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        extendedStyle = (extendedStyle & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtr(windowHandle, GwlExStyle, new nint(extendedStyle));

        var cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(windowHandle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(windowHandle, DwmwaBorderColor, ref borderColor, sizeof(uint));
        SetWindowPos(windowHandle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    private void InstallOutsideClickHook()
    {
        if (_mouseHook != 0)
        {
            return;
        }

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private void RemoveOutsideClickHook()
    {
        if (_mouseHook == 0)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }

    private nint MouseHookCallback(int code, nint message, nint data)
    {
        if (code >= 0
            && message.ToInt32() is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown
            && !_isClosed)
        {
            var mouse = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
            if (GetWindowRect(_windowHandle, out var bounds)
                && (mouse.Point.X < bounds.Left
                    || mouse.Point.X >= bounds.Right
                    || mouse.Point.Y < bounds.Top
                    || mouse.Point.Y >= bounds.Bottom))
            {
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isClosed)
                    {
                        Close();
                    }
                });
            }
        }

        return CallNextHookEx(_mouseHook, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private delegate nint LowLevelMouseProc(int code, nint message, nint data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowHandleInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint message, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint pvAttribute, int cbAttribute);
}
