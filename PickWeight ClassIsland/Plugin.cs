using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PickWeight.ClassIsland.Plugin;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<NotificationProviderAccessor>();
        services.AddNotificationProvider<PickWeightNotificationProvider, PickWeightNotificationSettingsControl>();
        services.AddHostedService<PickWeightPipeListenerService>();
    }
}
