using PickWeight.ClassIsland.Protocol;
using PickWeight_App.Services;

namespace PickWeight_App.Services;

/// <summary>
/// 监听 ClassIsland 课时状态：下课/放学时自动收尾——结束本轮并清空今日请假。
/// 启动时立即查一次，之后每 5 分钟复查；ClassIsland 未连接时静默跳过。
/// </summary>
public sealed class ClassIslandPeriodService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private readonly AppDataService _data;
    private readonly IClassIslandNotificationBridge _bridge;
    private readonly AppLogService _log;
    private CancellationTokenSource? _cancellation;
    private bool _wasOnClass;

    public ClassIslandPeriodService(
        AppDataService data,
        IClassIslandNotificationBridge bridge,
        AppLogService log)
    {
        _data = data;
        _bridge = bridge;
        _log = log;
    }

    /// <summary>启动周期检查。在应用启动后调用一次即可。</summary>
    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _ = RunLoopAsync(_cancellation.Token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // 单次检查失败不中断循环
            }

            try
            {
                await Task.Delay(CheckInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var result = await _bridge.QueryClassContextAsync(cancellationToken);
        if (!result.Succeeded)
        {
            // 版本不匹配时记录一次提示（通过日志与状态栏）
            if (result.IsVersionMismatch && !_versionMismatchNotified)
            {
                _versionMismatchNotified = true;
                await _log.InfoAsync("ClassIsland 插件版本过旧，课时信息不可用，请更新插件。", cancellationToken);
            }

            return;
        }

        if (result.Context is not { } context)
        {
            return; // 非上课时间，不做处理
        }

        var hasAbsent = _data.ActiveClassroom.Members.Any(member => member.AbsentOn is not null);
        var hasRoundProgress = _data.ActiveClassroom.Round.DrawnNames.Count > 0;
        var justEnded = _wasOnClass && !context.IsOnClass;

        if (justEnded && (hasAbsent || hasRoundProgress))
        {
            var classroom = _data.ActiveClassroom;
            if (hasRoundProgress)
            {
                classroom.Round.StartNextRound();
            }

            foreach (var member in classroom.Members)
            {
                member.AbsentOn = null;
            }

            await _data.SaveAsync(cancellationToken);
            await _log.InfoAsync("Class period ended; round restarted and today's absences cleared.", cancellationToken);
        }

        _wasOnClass = context.IsOnClass;
    }

    private bool _versionMismatchNotified;
}
