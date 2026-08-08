using System.Reflection;
using PickWeight.ClassIsland.Protocol;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;

namespace PickWeight.App.Tests;

[Collection("PickWeight.App serial")]
public sealed class LocalApiDrawCancellationTests
{
    [Fact]
    public async Task CancelBeforePrepare_DoesNotPrepareOrLeakLease()
    {
        using var fixture = LocalApiFixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExecuteAsync(fixture.Service, cancellation.Token));

        Assert.Equal(0, fixture.Execution.PrepareCount);
        Assert.Equal(0, fixture.Execution.DiscardCount);
        AssertLeaseAvailable(fixture.Coordinator);
    }

    [Fact]
    public async Task CancelAfterUiCallbackStarts_CompletesRequest()
    {
        using var fixture = LocalApiFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueue = new Func<Func<Task>, bool>(callback =>
        {
            _ = callback();
            return true;
        });

        var operation = InvokeDrawOnUiThreadAsync(
            fixture.Service,
            cancellation.Token,
            enqueue,
            () =>
            {
                callbackStarted.TrySetResult();
                cancellation.Cancel();
            });

        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));

        Assert.Equal(0, fixture.Execution.PrepareCount);
        Assert.Equal(0, fixture.Execution.DiscardCount);
        AssertLeaseAvailable(fixture.Coordinator);
    }

    [Fact]
    public async Task CancelAfterPrepare_RollsBackPreparedDrawAndReleasesLease()
    {
        using var fixture = LocalApiFixture.Create();
        fixture.Execution.BlockBeforeCommit = true;
        using var cancellation = new CancellationTokenSource();
        var operation = ExecuteAsync(fixture.Service, cancellation.Token);

        await fixture.Execution.CommitStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(1, fixture.Execution.DiscardCount);
        Assert.Empty(fixture.Data.ActiveClassroom.DrawHistory);
        Assert.Equal(0, fixture.Data.ActiveClassroom.Members[0].CalledCount);
        AssertLeaseAvailable(fixture.Coordinator);
    }

    [Fact]
    public async Task CancelAfterCommitApplied_KeepsCommittedState()
    {
        using var fixture = LocalApiFixture.Create();
        fixture.Execution.BlockAfterCommit = true;
        using var cancellation = new CancellationTokenSource();
        var operation = ExecuteAsync(fixture.Service, cancellation.Token);

        await fixture.Execution.CommitApplied.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotEmpty(fixture.Data.ActiveClassroom.DrawHistory);
        Assert.Equal(1, fixture.Data.ActiveClassroom.Members[0].CalledCount);
        cancellation.Cancel();

        var names = await operation.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(["张三"], names);
        Assert.Equal(0, fixture.Execution.DiscardCount);
        Assert.Single(fixture.Data.ActiveClassroom.DrawHistory);
        Assert.Equal(1, fixture.Data.ActiveClassroom.Members[0].CalledCount);
        AssertLeaseAvailable(fixture.Coordinator);
    }

    private static Task<IReadOnlyList<string>> ExecuteAsync(
        LocalApiService service,
        CancellationToken cancellationToken)
    {
        var method = typeof(LocalApiService).GetMethod(
            "ExecuteDrawOperationAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<IReadOnlyList<string>>)method!.Invoke(service, [cancellationToken])!;
    }

    private static Task<IReadOnlyList<string>> InvokeDrawOnUiThreadAsync(
        LocalApiService service,
        CancellationToken cancellationToken,
        Func<Func<Task>, bool> tryEnqueue,
        Action callbackStartedHook)
    {
        var method = typeof(LocalApiService).GetMethod(
            "DrawOnUiThreadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(CancellationToken), typeof(Func<Func<Task>, bool>), typeof(Action)],
            modifiers: null);
        Assert.NotNull(method);
        return (Task<IReadOnlyList<string>>)method!.Invoke(
            service,
            [cancellationToken, tryEnqueue, callbackStartedHook])!;
    }

    private static void AssertLeaseAvailable(DrawExecutionCoordinator coordinator)
    {
        using var lease = coordinator.TryAcquire();
        Assert.NotNull(lease);
    }

    private sealed class LocalApiFixture : IDisposable
    {
        private LocalApiFixture(
            AppDataService data,
            TestSettingsService settings,
            DrawExecutionCoordinator coordinator,
            CancellationExecutionService execution,
            LocalApiService service)
        {
            Data = data;
            Settings = settings;
            Coordinator = coordinator;
            Execution = execution;
            Service = service;
        }

        public AppDataService Data { get; }
        public TestSettingsService Settings { get; }
        public DrawExecutionCoordinator Coordinator { get; }
        public CancellationExecutionService Execution { get; }
        public LocalApiService Service { get; }

        public static LocalApiFixture Create()
        {
            var data = new AppDataService(new InMemoryStateStore(), new ConstantWeightProviderFactory());
            data.InitializeAsync().GetAwaiter().GetResult();
            data.ActiveClassroom.Members.Add(new Member { Name = "张三" });
            data.State.Settings.DrawMode = DrawMode.Weighted;
            data.State.Settings.DrawTargetCount = 1;
            var settings = new TestSettingsService(data);
            var coordinator = new DrawExecutionCoordinator();
            var inner = new DrawExecutionService(
                data,
                new DrawService(() => 0d),
                settings,
                new NotificationService(settings),
                new TestClassIslandNotificationBridge(),
                new ObsOverlayService(Path.Combine(Path.GetTempPath(), "PickWeight.Api.Tests", Guid.NewGuid().ToString("N"), "draw.txt")),
                new AppLogService());
            var execution = new CancellationExecutionService(inner);
            var quickDraw = new QuickDrawWindowService(settings, coordinator, new AppLogService());
            var service = new LocalApiService(
                data,
                settings,
                execution,
                quickDraw,
                coordinator,
                new AppLogService());
            return new LocalApiFixture(data, settings, coordinator, execution, service);
        }

        public void Dispose() => Service.Dispose();
    }

    private sealed class CancellationExecutionService : IDrawExecutionService
    {
        private readonly IDrawExecutionService _inner;

        public CancellationExecutionService(IDrawExecutionService inner) => _inner = inner;

        public TaskCompletionSource CommitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CommitApplied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockBeforeCommit { get; set; }
        public bool BlockAfterCommit { get; set; }
        public bool ThrowOnDiscard { get; set; }
        public int PrepareCount { get; private set; }
        public int DiscardCount { get; private set; }

        public async Task<PreparedDraw> PrepareAsync(
            DrawExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return await _inner.PrepareAsync(request, cancellationToken);
        }

        public async Task<DrawCommitOutcome> CommitAsync(
            PreparedDraw prepared,
            IEnumerable<Member>? members = null,
            bool confirmSelection = false,
            bool notify = true,
            bool allowOutsideSelection = false,
            CancellationToken cancellationToken = default)
        {
            CommitStarted.TrySetResult();
            if (BlockBeforeCommit)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var outcome = await _inner.CommitAsync(
                prepared,
                members,
                confirmSelection,
                notify,
                allowOutsideSelection,
                CancellationToken.None);
            CommitApplied.TrySetResult();
            if (BlockAfterCommit)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return outcome;
        }

        public Task RebuildCommittedStateAsync(
            PreparedDraw prepared,
            IEnumerable<Member> committedMembers,
            CancellationToken cancellationToken = default) =>
            _inner.RebuildCommittedStateAsync(prepared, committedMembers, cancellationToken);

        public async Task DiscardAsync(PreparedDraw prepared, CancellationToken cancellationToken = default)
        {
            DiscardCount++;
            if (ThrowOnDiscard)
            {
                throw new InvalidOperationException("rollback failed");
            }

            await _inner.DiscardAsync(prepared, cancellationToken);
        }

        public Task<DrawReplaceOutcome> ReplaceAsync(
            PreparedDraw prepared,
            Member replacedMember,
            CancellationToken cancellationToken = default) =>
            _inner.ReplaceAsync(prepared, replacedMember, cancellationToken);

        public Task<bool> RateAsync(
            PreparedDraw prepared,
            string memberName,
            DrawRating rating,
            CancellationToken cancellationToken = default) =>
            _inner.RateAsync(prepared, memberName, rating, cancellationToken);
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        private AppState _state = new();

        public Task<AppState> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_state.Clone());

        public Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
        {
            _state = state.Clone();
            return Task.CompletedTask;
        }

        public Task<Classroom> LoadClassroomAsync(string name, CancellationToken cancellationToken = default)
        {
            var classroom = _state.ClassroomNames.Contains(name)
                ? new Classroom { Name = name }
                : new Classroom();
            // 从内存班级字典读取
            if (_classrooms.TryGetValue(name, out var stored))
            {
                classroom = stored.Clone();
            }
            return Task.FromResult(classroom);
        }

        public Task SaveClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default)
        {
            _classrooms[classroom.Name] = classroom.Clone();
            if (!_state.ClassroomNames.Contains(classroom.Name))
            {
                _state.ClassroomNames.Add(classroom.Name);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListClassroomNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_state.ClassroomNames.ToList());

        public Task CreateClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default)
        {
            _classrooms[classroom.Name] = classroom.Clone();
            _state.ClassroomNames.Add(classroom.Name);
            return Task.CompletedTask;
        }

        public Task RenameClassroomAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        {
            if (_classrooms.TryGetValue(oldName, out var classroom))
            {
                _classrooms.Remove(oldName);
                classroom.Name = newName;
                _classrooms[newName] = classroom;
            }
            _state.ClassroomNames.Remove(oldName);
            _state.ClassroomNames.Add(newName);
            return Task.CompletedTask;
        }

        public Task DeleteClassroomAsync(string name, CancellationToken cancellationToken = default)
        {
            _classrooms.Remove(name);
            _state.ClassroomNames.Remove(name);
            return Task.CompletedTask;
        }

        public string GetClassroomDirectory(string name) => Path.Combine(Path.GetTempPath(), "in-memory", name);

    private readonly Dictionary<string, Classroom> _classrooms = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ConstantWeightProvider : IImplicitWeightProvider
    {
        public Task<ImplicitWeightLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImplicitWeightLoadResult(new Dictionary<string, double>(), false, 0, string.Empty));

        public Task<ImplicitWeightLoadResult> SynchronizeAsync(
            IReadOnlyCollection<Member> members,
            IReadOnlyDictionary<string, string>? renamedMembers = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImplicitWeightLoadResult(
                members.ToDictionary(member => member.Name, _ => 1d),
                false,
                0,
                string.Empty));
    }

    private sealed class TestClassIslandNotificationBridge : IClassIslandNotificationBridge
    {
        public Task<ClassIslandSendResult> ShowDrawResultAsync(
            IEnumerable<string> names,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClassIslandSendResult(true));

        public Task<ClassContextResult> QueryClassContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClassContextResult(false, null, "not connected"));
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private readonly AppDataService _data;

        public TestSettingsService(AppDataService data) => _data = data;

        public AppSettings Current => _data.State.Settings;
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

    private sealed class ConstantWeightProviderFactory : IImplicitWeightProviderFactory
    {
        public IImplicitWeightProvider Create(string classroomDirectory) =>
            new ConstantWeightProvider();
    }

}
