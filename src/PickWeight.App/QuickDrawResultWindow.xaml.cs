using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using PickWeight.Core.Models;
using PickWeight_App.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;

namespace PickWeight_App;

/// <summary>
/// 快速抽取结果窗口。支持单人与多人展示，所有抽取规则由调用方提供。
/// </summary>
public sealed partial class QuickDrawResultWindow : Window
{
    private const int CompactLogicalWidth = 440;
    private const int CompactLogicalHeight = 196;
    private const int AnimationDurationMilliseconds = 2500;
    private const int AnimationIntervalMinMilliseconds = 45;
    private const int AnimationIntervalMaxMilliseconds = 220;
    private const int RevealIntervalMilliseconds = 420;
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

    private readonly nint _windowHandle;
    private readonly TransientAcrylicBackdrop _acrylicBackdrop;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationStopwatch = new();
    private readonly Random _random = new();
    private TaskCompletionSource? _closedCompletion;
    private IReadOnlyList<string> _candidateNames = [];
    private IReadOnlyList<string> _rollingNames = [];
    private IReadOnlyList<string> _finalNames = [];
    private Func<CancellationToken, Task>? _commitAsync;
    private Func<CancellationToken, Task>? _cancelAsync;
    private CancellationToken _cancellationToken;
    private CancellationTokenRegistration _cancellationRegistration;
    private bool _isShown;
    private bool _animationCompleted;
    private bool _isCompletingAnimation;
    private bool _skipAnimation;
    private bool _isClosed;
    private bool _cancelInvoked;
    private int _revealIndex;
    private bool _hasPendingNameTransition;

    internal bool IsAnimationCompleted => _animationCompleted;
    internal bool HasCancelCallback => _cancelAsync is not null;

    public QuickDrawResultWindow()
    {
        InitializeComponent();

        Title = "快速抽取";
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
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationIntervalMinMilliseconds) };
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
        IReadOnlyList<string> rollingNames,
        IReadOnlyList<string> finalNames,
        Func<CancellationToken, Task> commitAsync,
        Func<CancellationToken, Task> cancelAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateNames);
        ArgumentNullException.ThrowIfNull(rollingNames);
        ArgumentNullException.ThrowIfNull(finalNames);
        ArgumentNullException.ThrowIfNull(commitAsync);
        ArgumentNullException.ThrowIfNull(cancelAsync);

        if (finalNames.Count == 0)
        {
            throw new ArgumentException("至少需要一个结果姓名。", nameof(finalNames));
        }

        if (_closedCompletion is not null)
        {
            throw new InvalidOperationException("此快速抽取窗口已显示。");
        }

        _rollingNames = rollingNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _candidateNames = candidateNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Concat(finalNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _finalNames = finalNames.Select(name => name.Trim()).ToList();
        _commitAsync = commitAsync;
        _cancelAsync = cancelAsync;
        _cancellationToken = cancellationToken;
        _closedCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cancellationRegistration = cancellationToken.Register(CloseFromCancellation);

        ConfigureMultiNameLayout();
        _revealIndex = 0;
        ShowNextName();
        ResizeAndCenter();
        _isShown = true;
        AppWindow.Show();
        Activate();
        App.GetService<Services.AnimationService>().PlayPopupEntrance(WindowRoot);
        _animationStopwatch.Restart();
        _animationTimer.Start();

        await _closedCompletion.Task;
    }

    private DataTemplate? _multiNameTemplate;
    private double _multiNameFontSize;

    /// <summary>按人数切换单人大字 / 多人网格布局。</summary>
    private void ConfigureMultiNameLayout()
    {
        var isMulti = _finalNames.Count > 1;
        NameText.Visibility = isMulti ? Visibility.Collapsed : Visibility.Visible;
        NamesList.Visibility = isMulti ? Visibility.Visible : Visibility.Collapsed;

        if (!isMulti)
        {
            NameText.FontSize = 44;
            return;
        }

        // 多人布局：字号随人数缩小
        var baseFont = Math.Clamp(64d / Math.Sqrt(_finalNames.Count), 24d, 44d);
        if (_multiNameTemplate is null || Math.Abs(_multiNameFontSize - baseFont) > 0.01d)
        {
            _multiNameFontSize = baseFont;
            _multiNameTemplate = CreateMultiNameTemplate(baseFont);
        }

        NamesList.ItemTemplate = _multiNameTemplate;
    }

    private DataTemplate CreateMultiNameTemplate(double fontSize)
    {
        var fontText = fontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var xaml = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            @"<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Border Margin=""8"" Padding=""20,12""
                        Background=""{{ThemeResource CardBackgroundFillColorDefaultBrush}}""
                        BorderBrush=""{{ThemeResource CardStrokeColorDefaultBrush}}""
                        BorderThickness=""1"" CornerRadius=""12"">
                    <TextBlock FontSize=""{0}""
                               FontWeight=""SemiBold""
                               Text=""{{Binding}}"" />
                </Border>
            </DataTemplate>",
            fontText);
        return (DataTemplate)XamlReader.Load(xaml);
    }

    private void ShowNextName()
    {
        if (_revealIndex >= _finalNames.Count)
        {
            return;
        }

        var name = _finalNames[_revealIndex];
        if (_finalNames.Count == 1)
        {
            NameText.Text = name;
        }
        else
        {
            NamesList.ItemsSource = _finalNames.Take(_revealIndex + 1).ToList();
        }

        _revealIndex++;
    }

    private void WindowRoot_Loaded(object sender, RoutedEventArgs e) => ResizeAndCenter();

    private void WindowRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        // 翻页笔操作：PageDown / PageRight / Space 跳过滚动动画直接定格
        if (e.Key == VirtualKey.PageDown || e.Key == VirtualKey.Right || e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            SkipAnimation();
        }
    }

    /// <summary>跳过滚动动画直接定格：置标志让下一个动画节拍立即进入完成分支。</summary>
    private void SkipAnimation()
    {
        if (_isClosed || _isCompletingAnimation || _animationCompleted)
        {
            return;
        }

        _skipAnimation = true;
        _animationStopwatch.Restart();
        _animationTimer.Start();
    }

    private void QuickDrawResultWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // 失焦自动关闭（加 800 ms 宽限期，避免弹出瞬间被抢焦点误关）
        if (_isShown && !_isClosed
            && args.WindowActivationState == WindowActivationState.Deactivated)
        {
            var window = this;
            App.DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(800);
                if (window._isShown && !window._isClosed)
                {
                    window.Close();
                }
            });
        }
    }

    private async void AnimationTimer_Tick(object? sender, object e)
    {
        if (_isClosed || _isCompletingAnimation)
        {
            return;
        }

        if (!_skipAnimation && _animationStopwatch.ElapsedMilliseconds < AnimationDurationMilliseconds)
        {
            // 滚动阶段：随机展示候选姓名
            if (_finalNames.Count == 1)
            {
                NameText.Text = NextCandidateName();
                if (_hasPendingNameTransition)
                {
                    App.GetService<Services.AnimationService>().PlayNameTick(NameText);
                }

                _hasPendingNameTransition = true;
            }
            else
            {
                // 多人：逐个揭晓
                var elapsed = _animationStopwatch.ElapsedMilliseconds;
                var revealCount = Math.Min(
                    _finalNames.Count,
                    1 + (int)(elapsed / RevealIntervalMilliseconds));
                if (revealCount != _revealIndex)
                {
                    _revealIndex = revealCount;
                    NamesList.ItemsSource = _finalNames.Take(revealCount).ToList();
                }
            }

            ScheduleNextAnimationTick();
            return;
        }

        _isCompletingAnimation = true;
        _animationTimer.Stop();
        _animationCompleted = true;
        _skipAnimation = false;

        // 定格最终结果
        if (_finalNames.Count == 1)
        {
            NameText.Text = _finalNames[0];
            App.GetService<Services.AnimationService>().PlayResultReveal(NameText, 0);
        }
        else
        {
            NamesList.ItemsSource = _finalNames.ToList();
            App.GetService<Services.AnimationService>().PlayResultReveal(NamesList, 0);
        }

        try
        {
            await _commitAsync!(_cancellationToken);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            await InvokeCancelAsync();
        }
        catch
        {
            await InvokeCancelAsync();
        }
        finally
        {
            _isCompletingAnimation = false;
        }
    }

    /// <summary>按缓动曲线动态安排下一拍，实现「越转越慢」的减速停止效果。</summary>
    private void ScheduleNextAnimationTick()
    {
        _animationTimer.Stop();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(
            ComputeNextInterval(_animationStopwatch.ElapsedMilliseconds));
        _animationTimer.Start();
    }

    /// <summary>
    /// 计算下一拍滚动间隔（毫秒）。ease-out quadratic 缓动：进度越接近 1，间隔越接近上限，
    /// 实现「越转越慢」的减速落地观感。抽为纯函数以便单元测试锁定滚动阶段换名次数。
    /// </summary>
    internal static int ComputeNextInterval(long elapsedMilliseconds)
    {
        var progress = Math.Clamp(
            (double)elapsedMilliseconds / AnimationDurationMilliseconds,
            0d,
            1d);
        var eased = 1d - Math.Pow(1d - progress, 2d);
        return AnimationIntervalMinMilliseconds
            + (int)((AnimationIntervalMaxMilliseconds - AnimationIntervalMinMilliseconds) * eased);
    }

    private string NextCandidateName()
    {
        // 滚动池优先：全班启用成员，让滚动观感更丰富；空则回退候选名，再回退最终名。
        if (_rollingNames.Count > 0)
        {
            return _rollingNames[_random.Next(_rollingNames.Count)];
        }

        if (_candidateNames.Count == 0)
        {
            return _finalNames[0];
        }

        return _candidateNames[_random.Next(_candidateNames.Count)];
    }

    private void ResizeAndCenter()
    {
        var scale = WindowRoot.XamlRoot?.RasterizationScale
            ?? Math.Max(1d, GetDpiForWindow(_windowHandle) / 96d);
        var logicalWidth = CompactLogicalWidth;
        var logicalHeight = CompactLogicalHeight;
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
