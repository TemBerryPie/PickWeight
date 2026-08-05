using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using PickWeight.Core.Models;
using WinRT;

namespace PickWeight_App.Services;

/// <summary>
/// Hosts a configurable Desktop Acrylic controller for small transient windows.
/// The low tint values deliberately keep the blurred desktop visible instead of
/// turning the material into an opaque dark surface.
/// </summary>
public sealed class TransientAcrylicBackdrop : IDisposable
{
    private readonly Window _window;
    private readonly SystemBackdropConfiguration? _configuration;
    private readonly DesktopAcrylicController? _controller;
    private bool _disposed;

    public TransientAcrylicBackdrop(Window window)
    {
        _window = window;
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Default
        };
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
        _controller.SetSystemBackdropConfiguration(_configuration);
        _window.Closed += Window_Closed;
    }

    public void Apply(AppSettings settings)
    {
        if (_controller is null || _configuration is null)
        {
            return;
        }

        var useDarkTint = settings.Theme == AppTheme.Dark
            || (settings.Theme == AppTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        _configuration.Theme = settings.Theme switch
        {
            AppTheme.Light => SystemBackdropTheme.Light,
            AppTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default
        };

        var amount = (float)Math.Clamp(settings.AcrylicBlurAmount, 0d, 1d);
        _controller.TintColor = useDarkTint
            ? Windows.UI.Color.FromArgb(255, 12, 12, 12)
            : Windows.UI.Color.FromArgb(255, 252, 252, 252);
        _controller.TintOpacity = 0.025f + amount * 0.035f;
        _controller.LuminosityOpacity = 0.08f + amount * 0.08f;
        _controller.FallbackColor = useDarkTint
            ? Windows.UI.Color.FromArgb(220, 24, 24, 24)
            : Windows.UI.Color.FromArgb(220, 246, 246, 246);
    }

    private void Window_Closed(object sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Closed -= Window_Closed;
        _controller?.RemoveAllSystemBackdropTargets();
        _controller?.Dispose();
    }
}
