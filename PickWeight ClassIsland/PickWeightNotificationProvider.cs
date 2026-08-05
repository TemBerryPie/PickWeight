using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using PickWeight.ClassIsland.Protocol;

namespace PickWeight.ClassIsland.Plugin;

[NotificationProviderInfo(
    "DC3C4DB4-4CF3-40A9-871B-37E102678251",
    "PickWeight 点名结果",
    "\ue02a",
    "显示来自 PickWeight 的抽取结果。")]
public sealed class PickWeightNotificationProvider : NotificationProviderBase<PickWeightNotificationSettings>
{
    public PickWeightNotificationProvider(NotificationProviderAccessor accessor)
    {
        accessor.Provider = this;
    }

    public void ShowDrawResult(IReadOnlyList<string> names)
    {
        var message = ClassIslandProtocol.FormatDrawResult(names);
        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateSimpleTextContent(message, content =>
            {
                content.Duration = GetDuration(Settings.MaskDurationSeconds, 5d);
                content.IsSpeechEnabled = Settings.IsMaskSpeechEnabled;
            }),
            OverlayContent = Settings.ShowOverlay
                ? NotificationContent.CreateSimpleTextContent(message, content =>
                {
                    content.Duration = GetDuration(Settings.OverlayDurationSeconds, 10d);
                    content.IsSpeechEnabled = Settings.IsOverlaySpeechEnabled;
                })
                : null
        });
    }

    private static TimeSpan GetDuration(double seconds, double fallback) =>
        TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 0.5d, 120d) : fallback);
}
