using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;

namespace PickWeight.ClassIsland.Plugin;

public sealed partial class PickWeightNotificationSettingsControl
    : NotificationProviderControlBase<PickWeightNotificationSettings>
{
    public PickWeightNotificationSettingsControl()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
