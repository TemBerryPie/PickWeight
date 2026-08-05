using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PickWeight.Core.Models;
using PickWeight_App.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;

namespace PickWeight_App;

public sealed partial class TrayMenuWindow : Window
{
    private const int MenuLogicalWidth = 244;
    private const int MenuLogicalHeight = 128;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpFrameChanged = 0x0020;
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

    private readonly nint _windowHandle;
    private MenuFlyoutItem? _firstItem;
    private bool _isVisible;

    public TrayMenuWindow()
    {
        InitializeComponent();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        BuildMenuItems();

        AppWindow.Resize(GetMenuSize());
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        ConfigureNativeWindow(_windowHandle);
        Activated += TrayMenuWindow_Activated;
    }

    public void ShowAtCursor(AppSettings settings)
    {
        ApplyAppearance(settings);
        GetCursorPos(out var cursorPoint);
        var menuSize = GetMenuSize();
        AppWindow.Resize(menuSize);
        AppWindow.Move(GetMenuPosition(cursorPoint, menuSize));
        _isVisible = true;
        AppWindow.Show();
        Activate();
        if (_firstItem is null || !_firstItem.Focus(FocusState.Programmatic))
        {
            WindowRoot.Focus(FocusState.Programmatic);
        }
    }

    public void HideMenu()
    {
        if (!_isVisible)
        {
            return;
        }

        _isVisible = false;
        AppWindow.Hide();
    }

    private void ApplyAppearance(AppSettings settings)
    {
        WindowRoot.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void BuildMenuItems()
    {
        MenuItemsPanel.Children.Clear();
        _firstItem = CreateMenuItem("\uE8A7", "显示主窗口", ShowMainWindow_Click);
        MenuItemsPanel.Children.Add(_firstItem);
        MenuItemsPanel.Children.Add(CreateMenuItem("\uE77B", "立即点名", QuickDraw_Click));
        MenuItemsPanel.Children.Add(new MenuFlyoutSeparator());
        MenuItemsPanel.Children.Add(CreateMenuItem("\uE7E8", "退出", Exit_Click));
    }

    private static MenuFlyoutItem CreateMenuItem(string glyph, string text, RoutedEventHandler click)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            MinWidth = MenuLogicalWidth - 8,
            Icon = new FontIcon
            {
                FontSize = 16,
                Glyph = glyph
            }
        };
        item.Click += click;
        return item;
    }

    private SizeInt32 GetMenuSize()
    {
        var scale = WindowRoot.XamlRoot?.RasterizationScale ?? Math.Max(1d, GetDpiForWindow(_windowHandle) / 96d);
        MenuSurface.Measure(new Windows.Foundation.Size(MenuLogicalWidth, double.PositiveInfinity));
        var logicalHeight = Math.Max(MenuLogicalHeight, MenuSurface.DesiredSize.Height);
        return new SizeInt32(
            (int)Math.Ceiling(MenuLogicalWidth * scale),
            (int)Math.Ceiling(logicalHeight * scale));
    }

    private static PointInt32 GetMenuPosition(NativePoint cursorPoint, SizeInt32 menuSize)
    {
        var displayArea = DisplayArea.GetFromPoint(
            new PointInt32(cursorPoint.X, cursorPoint.Y),
            DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var maxX = workArea.X + workArea.Width - menuSize.Width;
        var maxY = workArea.Y + workArea.Height - menuSize.Height;
        var x = maxX < workArea.X
            ? workArea.X
            : Math.Clamp(cursorPoint.X - menuSize.Width + 16, workArea.X, maxX);
        var y = cursorPoint.Y + menuSize.Height > workArea.Y + workArea.Height
            ? cursorPoint.Y - menuSize.Height - 8
            : cursorPoint.Y + 8;
        y = maxY < workArea.Y ? workArea.Y : Math.Clamp(y, workArea.Y, maxY);
        return new PointInt32(x, y);
    }

    private void TrayMenuWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isVisible && args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideMenu();
        }
    }

    private void ShowMainWindow_Click(object sender, RoutedEventArgs e) =>
        RunAfterHide(App.GetService<TrayIconService>().ShowMainWindow);

    private async void QuickDraw_Click(object sender, RoutedEventArgs e)
    {
        HideMenu();
        await App.GetService<QuickDrawWindowService>().RequestQuickDrawAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) =>
        RunAfterHide(App.GetService<TrayIconService>().ExitApplication);

    private void RunAfterHide(Action action)
    {
        HideMenu();
        action();
    }

    private void WindowRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            HideMenu();
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint windowHandle, nint windowHandleInsertAfter, int x, int y, int width, int height, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint pvAttribute, int cbAttribute);
}
