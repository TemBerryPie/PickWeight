using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

public sealed class TrayMenuWindowService
{
    private readonly ISettingsService _settings;
    private TrayMenuWindow? _window;
    private bool _isShowQueued;

    public TrayMenuWindowService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void Show()
    {
        if (_isShowQueued)
        {
            return;
        }

        _isShowQueued = true;
        if (!App.DispatcherQueue.TryEnqueue(() =>
        {
            _isShowQueued = false;
            _window ??= new TrayMenuWindow();
            _window.ShowAtCursor(_settings.Current);
        }))
        {
            _isShowQueued = false;
        }
    }

    public void Hide() => _window?.HideMenu();

    public void Close()
    {
        _window?.Close();
        _window = null;
    }
}
