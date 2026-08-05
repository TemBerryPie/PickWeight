using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight_App.Services;

public sealed class ThemeService
{
    private readonly ISettingsService _settings;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, args) =>
        {
            if (args.AffectsMainAppearance)
            {
                Apply();
            }
        };
    }

    public void Apply()
    {
        if (App.Window.Content is FrameworkElement root)
        {
            root.RequestedTheme = _settings.Current.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        App.Window.SystemBackdrop = _settings.Current.BackgroundEffect == AppBackgroundEffect.Mica
            ? new MicaBackdrop()
            : new DesktopAcrylicBackdrop();
        if (App.Window is MainWindow mainWindow)
        {
            mainWindow.ApplyAppearance(_settings.Current);
        }
    }
}
