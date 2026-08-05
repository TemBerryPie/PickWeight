using PickWeight.ClassIsland.Protocol;
using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

public interface IClassIslandNotificationBridge
{
    Task<ClassIslandSendResult> ShowDrawResultAsync(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default);
}

public sealed class ClassIslandNotificationBridge : IClassIslandNotificationBridge
{
    private readonly ISettingsService _settings;
    private readonly ClassIslandPipeClient _client = new();

    public ClassIslandNotificationBridge(ISettingsService settings)
    {
        _settings = settings;
    }

    public Task<ClassIslandSendResult> ShowDrawResultAsync(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        return !_settings.Current.ShowClassIslandNotifications
            ? Task.FromResult(new ClassIslandSendResult(true))
            : _client.SendDrawResultAsync(names, cancellationToken);
    }
}
