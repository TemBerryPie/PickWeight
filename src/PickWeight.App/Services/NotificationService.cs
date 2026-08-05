using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace PickWeight_App.Services;

public sealed class NotificationService
{
    private readonly PickWeight.Core.Abstractions.ISettingsService _settings;
    private bool _isRegistered;

    public NotificationService(PickWeight.Core.Abstractions.ISettingsService settings)
    {
        _settings = settings;
    }

    public void Register()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            if (!AppNotificationManager.IsSupported())
            {
                return;
            }

            manager.Register();
            _isRegistered = true;
        }
        catch
        {
            _isRegistered = false;
        }
    }

    public void ShowDrawResult(IEnumerable<string> names)
    {
        if (!_isRegistered || !_settings.Current.ShowDrawNotifications)
        {
            return;
        }

        var nameList = string.Join("、", names);
        if (string.IsNullOrWhiteSpace(nameList))
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("点名结果")
                .AddText(nameList)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            _isRegistered = false;
        }
    }

    public void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
        }
        _isRegistered = false;
    }
}
