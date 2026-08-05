using PickWeight.Tray;

namespace PickWeight_App.Services;

public sealed class TrayIconService : IDisposable
{
    private TrayHost? _icon;
    public bool IsExitRequested { get; private set; }

    public void Initialize()
    {
        if (_icon is not null) return;
        _icon = new TrayHost(
            App.IconPath,
            ShowMainWindow,
            ShowTrayMenu);
    }

    public void ShowTrayMenu()
    {
        App.GetService<TrayMenuWindowService>().Show();
    }

    public void ShowMainWindow()
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            App.GetService<TrayMenuWindowService>().Hide();
            App.Window.AppWindow.Show();
            App.Window.Activate();
        });
    }

    public void ShowMainWindowAndDraw()
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            App.GetService<TrayMenuWindowService>().Hide();
            if (App.Window is MainWindow mainWindow)
            {
                await mainWindow.ShowAndDrawAsync();
            }
        });
    }

    public void ExitApplication()
    {
        IsExitRequested = true;
        App.DispatcherQueue.TryEnqueue(() =>
        {
            App.GetService<TrayMenuWindowService>().Hide();
            Dispose();
            App.GetService<QuickDrawWindowService>().Close();
            App.GetService<TrayMenuWindowService>().Close();
            App.GetService<NotificationService>().Unregister();
            App.Window.Close();
        });
    }

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Dispose();
        _icon = null;
    }
}
