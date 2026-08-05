using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PickWeight.Core.Models;
using PickWeight_App.Services;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PickWeight_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private nint _windowIcon;
    private readonly SemaphoreSlim _windowStateSaveGate = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(App.IconPath);
        SetTaskbarIcon();
        RestoreWindowBounds();
        AppWindow.Changed += (sender, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                _ = SaveWindowBoundsAsync();
            }
        };
        AppWindow.Closing += (_, args) =>
        {
            if (!App.GetService<TrayIconService>().IsExitRequested)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
        Closed += (_, _) =>
        {
            if (_windowIcon != 0)
            {
                DestroyIcon(_windowIcon);
                _windowIcon = 0;
            }
        };

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void SetTaskbarIcon()
    {
        _windowIcon = LoadImage(0, App.IconPath, ImageIcon, 0, 0, LrLoadFromFile);
        if (_windowIcon == 0)
        {
            return;
        }

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SendMessage(windowHandle, WmSetIcon, IconBig, _windowIcon);
        SendMessage(windowHandle, WmSetIcon, IconSmall, _windowIcon);
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
        var tintColor = useDarkTint
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 248, 248, 248);
        if (settings.BackgroundEffect == AppBackgroundEffect.Mica)
        {
            WindowRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        else
        {
            var amount = Math.Clamp(settings.AcrylicBlurAmount, 0d, 1d);
            var overlayAlpha = (byte)Math.Clamp(Math.Round(amount * 180), 0, 220);
            WindowRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                overlayAlpha,
                tintColor.R,
                tintColor.G,
                tintColor.B));
        }

        AppWindow.TitleBar.ButtonForegroundColor = useDarkTint
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0);
    }

    public async Task ShowAndDrawAsync()
    {
        AppWindow.Show();
        Activate();
        if (RootFrame.Content is MainPage mainPage)
        {
            await mainPage.NavigateToDrawAndDrawAsync();
        }
    }

    private void RestoreWindowBounds()
    {
        var settings = App.GetService<PickWeight.Core.Abstractions.ISettingsService>().Current;
        var width = settings.WindowWidth is >= 640 and <= 3840 ? settings.WindowWidth.Value : 1100;
        var height = settings.WindowHeight is >= 480 and <= 2160 ? settings.WindowHeight.Value : 760;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        if (settings.WindowX is >= 0 && settings.WindowY is >= 0)
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(settings.WindowX.Value, settings.WindowY.Value));
        }
    }

    private async Task SaveWindowBoundsAsync()
    {
        await _windowStateSaveGate.WaitAsync();
        try
        {
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            await App.GetService<PickWeight.Core.Abstractions.ISettingsService>().UpdateAsync(settings =>
            {
                settings.WindowX = position.X;
                settings.WindowY = position.Y;
                settings.WindowWidth = size.Width;
                settings.WindowHeight = size.Height;
            });
        }
        finally { _windowStateSaveGate.Release(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
