using PickWeight.ClassIsland.Protocol;
using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

public interface IClassIslandNotificationBridge
{
    Task<ClassIslandSendResult> ShowDrawResultAsync(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default);

    /// <summary>查询当前课时上下文。插件未连接或版本不匹配时返回失败结果。</summary>
    Task<ClassContextResult> QueryClassContextAsync(CancellationToken cancellationToken = default);
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

    public Task<ClassContextResult> QueryClassContextAsync(CancellationToken cancellationToken = default) =>
        _client.QueryContextAsync(cancellationToken);
}
