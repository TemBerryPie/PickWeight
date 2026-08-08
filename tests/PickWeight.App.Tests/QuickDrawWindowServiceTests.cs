using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight_App;
using PickWeight_App.Services;

namespace PickWeight.App.Tests;

[Collection("PickWeight.App serial")]
public sealed class QuickDrawWindowServiceTests
{
    [Fact]
    public async Task DrawExecutionCoordinator_SerializesCrossEntryOperations()
    {
        var coordinator = new DrawExecutionCoordinator();
        using var first = coordinator.TryAcquire();

        Assert.NotNull(first);
        Assert.Null(coordinator.TryAcquire());

        var waiting = coordinator.AcquireAsync();
        Assert.False(waiting.IsCompleted);
        first!.Dispose();

        using var second = await waiting;
        Assert.Null(coordinator.TryAcquire());
    }

    [Fact]
    public async Task DrawExecutionCoordinator_CancellationDoesNotAcquireLease()
    {
        var coordinator = new DrawExecutionCoordinator();
        using var first = coordinator.TryAcquire();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.AcquireAsync(cancellation.Token));
        Assert.Null(coordinator.TryAcquire());
    }

    [Fact]
    public async Task QuickCancel_ReleasesLeaseWithoutRollingBackNextDraw()
    {
        var coordinator = new DrawExecutionCoordinator();
        using var quickLease = coordinator.TryAcquire();
        var waitingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = new List<string>();
        var nextDraw = Task.Run(async () =>
        {
            waitingStarted.TrySetResult();
            using var lease = await coordinator.AcquireAsync();
            history.Add("normal");
        });

        await waitingStarted.Task;
        Assert.Empty(history);
        quickLease!.Dispose();
        await nextDraw;

        Assert.Equal(["normal"], history);
    }

    [Fact]
    public void AcceptedResult_IsMarkedAccepted()
    {
        var result = new QuickDrawRequestResult(QuickDrawRequestStatus.Accepted);

        Assert.Equal(QuickDrawRequestStatus.Accepted, result.Status);
        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void RollingSchedule_ProducesAtLeastEighteenNameChanges()
    {
        // 与 QuickDrawResultWindow 调度逻辑完全一致的离散递归模拟：
        // 首次节拍在启动后最小间隔触发，之后每拍按 ComputeNextInterval 重排。
        // 用户可见需求「滚动阶段换名 ≥18 次」被钉死，防止未来调参回归。
        const long durationMilliseconds = 2500;
        const int minIntervalMilliseconds = 45;
        var elapsedMilliseconds = minIntervalMilliseconds;
        var nameChanges = 0;
        while (elapsedMilliseconds < durationMilliseconds)
        {
            nameChanges++;
            elapsedMilliseconds += QuickDrawResultWindow.ComputeNextInterval(elapsedMilliseconds);
        }

        Assert.True(
            nameChanges >= 18,
            $"滚动阶段换名 {nameChanges} 次，低于 18 次下限。");
        // 当前 quad 缓动下预期恰好 19 次，精确断言可在调参时提前暴露变化。
        Assert.Equal(19, nameChanges);
    }

    [Fact]
    public async Task TryStartQuickDrawAsync_WithoutHandlerReportsUnavailable()
    {
        var service = new QuickDrawWindowService(new TestSettingsService());

        var result = await service.TryStartQuickDrawAsync();

        Assert.Equal(QuickDrawRequestStatus.Unavailable, result.Status);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public async Task TryStartQuickDrawAsync_CancellationReportsCanceled()
    {
        var service = new QuickDrawWindowService(new TestSettingsService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.TryStartQuickDrawAsync(cancellation.Token);

        Assert.Equal(QuickDrawRequestStatus.Canceled, result.Status);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public async Task TryStartQuickDrawAsync_IsBusyWhileAnotherEntryOwnsCoordinator()
    {
        var coordinator = new DrawExecutionCoordinator();
        using var externalLease = coordinator.TryAcquire();
        var prepared = false;
        var service = new QuickDrawWindowService(new TestSettingsService(), coordinator, new AppLogService())
        {
            QuickDrawRequested = _ =>
            {
                prepared = true;
                return Task.FromResult<QuickDrawPresentation?>(null);
            }
        };

        var result = await service.TryStartQuickDrawAsync();

        Assert.Equal(QuickDrawRequestStatus.Busy, result.Status);
        Assert.False(prepared);
    }

    [Fact]
    public async Task TryStartQuickDrawAsync_WhenDispatcherUnavailableDiscardsPreparedDraw()
    {
        if (PickWeight_App.App.DispatcherQueue is not null)
        {
            return;
        }

        var discarded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new DrawExecutionCoordinator();
        var service = new QuickDrawWindowService(new TestSettingsService(), coordinator, new AppLogService())
        {
            QuickDrawRequested = _ => Task.FromResult<QuickDrawPresentation?>(new QuickDrawPresentation(
                ["张三"],
                ["张三"],
                ["张三"],
                _ => Task.CompletedTask,
                _ =>
                {
                    discarded.TrySetResult();
                    return Task.CompletedTask;
                }))
        };

        var result = await service.TryStartQuickDrawAsync();

        Assert.Equal(QuickDrawRequestStatus.Accepted, result.Status);
        await discarded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        DrawExecutionLease? releasedLease = null;
        for (var attempt = 0; attempt < 30 && releasedLease is null; attempt++)
        {
            releasedLease = coordinator.TryAcquire();
            if (releasedLease is null)
            {
                await Task.Delay(10);
            }
        }

        using (releasedLease)
        {
            Assert.NotNull(releasedLease);
        }
    }

    [Fact]
    public async Task TryStartQuickDrawAsync_ReportsBusyWhileAnotherRequestPrepares()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuickDrawWindowService(new TestSettingsService())
        {
            QuickDrawRequested = async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return null;
            }
        };

        var first = service.TryStartQuickDrawAsync();
        await entered.Task;
        var second = await service.TryStartQuickDrawAsync();

        Assert.Equal(QuickDrawRequestStatus.Busy, second.Status);
        release.TrySetResult();
        var firstResult = await first;
        Assert.Equal(QuickDrawRequestStatus.NoCandidates, firstResult.Status);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public event EventHandler<SettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task UpdateAsync(Action<AppSettings> update)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }
}
