using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace PickWeight_App;

public sealed partial class QuickDrawWindow : Window
{
    private const int LogicalWidth = 56;
    private const int LogicalHeight = 56;
    private const double DragThreshold = 10d;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private readonly nint _windowHandle;
    private readonly Services.TransientAcrylicBackdrop _acrylicBackdrop;
    private uint? _pressedPointerId;
    private PointInt32 _pressedScreenPosition;
    private PointInt32 _pressedWindowPosition;
    private bool _isDragging;
    private bool _suppressNextTap;
    private double _touchTranslationX;
    private double _touchTranslationY;
    private double _lastRasterizationScale;

    public QuickDrawWindow()
    {
        InitializeComponent();

        _acrylicBackdrop = new Services.TransientAcrylicBackdrop(this);
        AppWindow.SetIcon(App.IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(LogicalWidth, LogicalHeight));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(_windowHandle, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsBorder | WsDlgFrame);
        SetWindowLongPtr(_windowHandle, GwlStyle, new nint(style));
        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
        extendedStyle = (extendedStyle & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtr(_windowHandle, GwlExStyle, new nint(extendedStyle));
        SetWindowPos(
            _windowHandle,
            0,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        var cornerPreference = DwmWindowCornerPreferenceRound;
        DwmSetWindowAttribute(
            _windowHandle,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(
            _windowHandle,
            DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());
    }

    public void ApplyAppearance(AppSettings settings)
    {
        WindowRoot.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        var useDarkTint = settings.Theme == AppTheme.Dark
            || (settings.Theme == AppTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        _acrylicBackdrop.Apply(settings);
        DrawIcon.Foreground = new SolidColorBrush(useDarkTint
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(245, 16, 16, 16));
    }

    private void WindowRoot_Loaded(object sender, RoutedEventArgs e)
    {
        ResizeForCurrentScale();
        WindowRoot.XamlRoot.Changed += (_, _) => ResizeForCurrentScale();
        App.GetService<Services.AnimationService>().PlayPopupEntrance(WindowRoot);
    }

    private void WindowRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(WindowRoot);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
        {
            _touchTranslationX = 0d;
            _touchTranslationY = 0d;
            _isDragging = false;
            return;
        }

        _pressedPointerId = e.Pointer.PointerId;
        _pressedScreenPosition = GetMouseScreenPosition();
        _pressedWindowPosition = AppWindow.Position;
        _isDragging = false;
        WindowRoot.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void WindowRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var currentPosition = GetMouseScreenPosition();
        var deltaX = currentPosition.X - _pressedScreenPosition.X;
        var deltaY = currentPosition.Y - _pressedScreenPosition.Y;
        var scale = WindowRoot.XamlRoot?.RasterizationScale ?? 1d;
        if (!_isDragging && Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < DragThreshold * scale)
        {
            return;
        }

        _isDragging = true;
        _suppressNextTap = true;
        AppWindow.Move(new PointInt32(
            _pressedWindowPosition.X + deltaX,
            _pressedWindowPosition.Y + deltaY));
        e.Handled = true;
    }

    private void WindowRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedPointerId != e.Pointer.PointerId)
        {
            return;
        }

        ResetPointerState(e);
        e.Handled = true;
    }

    private void WindowRoot_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        ResetPointerState(e);

    private void WindowRoot_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        ResetPointerState(e);

    private void WindowRoot_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _touchTranslationX = 0d;
        _touchTranslationY = 0d;
        _isDragging = false;
    }

    private void WindowRoot_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        var scale = WindowRoot.XamlRoot?.RasterizationScale ?? 1d;
        if (!_isDragging)
        {
            _touchTranslationX += e.Delta.Translation.X;
            _touchTranslationY += e.Delta.Translation.Y;
            if (Math.Sqrt(
                    _touchTranslationX * _touchTranslationX
                    + _touchTranslationY * _touchTranslationY) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
            _suppressNextTap = true;
            MoveBy(_touchTranslationX * scale, _touchTranslationY * scale);
            return;
        }

        _suppressNextTap = true;
        MoveBy(e.Delta.Translation.X * scale, e.Delta.Translation.Y * scale);
    }

    private async void WindowRoot_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            e.Handled = true;
            return;
        }

        if (!_isDragging)
        {
            await App.GetService<Services.QuickDrawWindowService>().RequestQuickDrawAsync();
        }
    }

    private async void QuickDrawMenu_Draw_Click(object sender, RoutedEventArgs e) =>
        await App.GetService<Services.QuickDrawWindowService>().RequestQuickDrawAsync();

    private void QuickDrawMenu_ShowMain_Click(object sender, RoutedEventArgs e) =>
        App.GetService<Services.TrayIconService>().ShowMainWindow();

    private async void QuickDrawMenu_Move_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "移动快捷按钮",
            Content = "按住快捷按钮并拖动即可移动位置。",
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
    }

    private async void QuickDrawMenu_Hide_Click(object sender, RoutedEventArgs e)
    {
        await App.GetService<ISettingsService>().UpdateAsync(settings =>
        {
            settings.ShowQuickDrawButton = false;
        });
    }

    private void MoveBy(double deltaX, double deltaY)
    {
        var position = AppWindow.Position;
        AppWindow.Move(new PointInt32(
            position.X + (int)Math.Round(deltaX),
            position.Y + (int)Math.Round(deltaY)));
    }

    private void ResizeForCurrentScale()
    {
        var scale = WindowRoot.XamlRoot?.RasterizationScale ?? 1d;
        if (Math.Abs(scale - _lastRasterizationScale) < 0.001d)
        {
            return;
        }

        _lastRasterizationScale = scale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(LogicalWidth * scale),
            (int)Math.Ceiling(LogicalHeight * scale)));
    }

    private void ResetPointerState(PointerRoutedEventArgs e)
    {
        _pressedPointerId = null;
        _isDragging = false;
        WindowRoot.ReleasePointerCapture(e.Pointer);
    }

    private static PointInt32 GetMouseScreenPosition()
    {
        GetCursorPos(out var cursorPosition);
        return new PointInt32(cursorPosition.X, cursorPosition.Y);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

}
