using PickWeight.ClassIsland.Protocol;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;

namespace PickWeight.App.Tests;

[Collection("PickWeight.App serial")]
public sealed class DrawCommitIdempotencyTests
{
    [Fact]
    public async Task CommitAsync_SamePreparedDraw_DoesNotDoubleCommit()
    {
        using var fixture = DrawCommitFixture.Create();

        var prepared = await fixture.Execution.PrepareAsync(new DrawExecutionRequest(
            TargetCount: 1,
            Filters: new DrawFilterOptions(),
            Mode: DrawMode.Weighted));
        var first = await fixture.Execution.CommitAsync(
            prepared,
            prepared.SelectedMembers,
            confirmSelection: true);
        var member = fixture.Data.ActiveClassroom.Members[0];
        var firstHistoryCount = fixture.Data.ActiveClassroom.DrawHistory.Count;

        var second = await fixture.Execution.CommitAsync(
            prepared,
            prepared.SelectedMembers,
            confirmSelection: true);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1, member.CalledCount);
        Assert.Equal(firstHistoryCount, fixture.Data.ActiveClassroom.DrawHistory.Count);
        Assert.Equal(first.CommittedNames, second.CommittedNames);
    }

    [Fact]
    public async Task PrepareAsync_NoRepeatDisabled_FullDrawnPool_StillDraws()
    {
        using var fixture = DrawCommitFixture.Create();
        fixture.Settings.Current.DrawNoRepeatInRound = false;
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "李四" });
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "王五" });
        var round = fixture.Data.ActiveClassroom.Round;
        round.DrawnNames.Add("张三");
        round.DrawnNames.Add("李四");
        round.DrawnNames.Add("王五");

        var prepared = await fixture.Execution.PrepareAsync(new DrawExecutionRequest(
            TargetCount: 1,
            Filters: new DrawFilterOptions(),
            Mode: DrawMode.Weighted));

        Assert.True(prepared.HasCandidates);
        Assert.Single(prepared.SelectedMembers);
        Assert.Equal("张三", prepared.SelectedMembers[0].Name);
        Assert.Equal(1, prepared.RoundNumber);
        Assert.Equal(["张三", "李四", "王五"], round.DrawnNames);
    }

    [Fact]
    public async Task PrepareAsync_NoRepeatEnabled_ExcludesRoundDrawnNames()
    {
        using var fixture = DrawCommitFixture.Create();
        fixture.Settings.Current.DrawNoRepeatInRound = true;
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "李四" });
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "王五" });
        var round = fixture.Data.ActiveClassroom.Round;
        round.DrawnNames.Add("张三");

        var prepared = await fixture.Execution.PrepareAsync(new DrawExecutionRequest(
            TargetCount: 1,
            Filters: new DrawFilterOptions(),
            Mode: DrawMode.Weighted));

        Assert.DoesNotContain(prepared.CandidateNames, name => name == "张三");
        Assert.DoesNotContain(prepared.SelectedMembers, member => member.Name == "张三");
        Assert.Equal(["李四", "王五"], prepared.CandidateNames);
        Assert.Equal("李四", prepared.SelectedMembers[0].Name);
        Assert.Equal(1, prepared.RoundNumber);
    }

    [Fact]
    public async Task PrepareAsync_NoRepeatEnabled_ExhaustedPool_RollsOverAndClears()
    {
        using var fixture = DrawCommitFixture.Create();
        fixture.Settings.Current.DrawNoRepeatInRound = true;
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "李四" });
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "王五" });
        var round = fixture.Data.ActiveClassroom.Round;
        round.RoundNumber = 5;
        round.DrawnNames.Add("张三");
        round.DrawnNames.Add("李四");
        round.DrawnNames.Add("王五");

        var prepared = await fixture.Execution.PrepareAsync(new DrawExecutionRequest(
            TargetCount: 1,
            Filters: new DrawFilterOptions(),
            Mode: DrawMode.Weighted));

        Assert.Equal(6, prepared.RoundNumber);
        Assert.Empty(round.DrawnNames);
        Assert.True(prepared.HasCandidates);
        Assert.Single(prepared.SelectedMembers);
    }

    [Fact]
    public async Task ReplaceAsync_NoRepeatDisabled_FullDrawnPool_CanReplace()
    {
        using var fixture = DrawCommitFixture.Create();
        fixture.Settings.Current.DrawNoRepeatInRound = false;
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "李四" });
        fixture.Data.ActiveClassroom.Members.Add(new Member { Name = "王五" });
        var round = fixture.Data.ActiveClassroom.Round;
        round.DrawnNames.Add("张三");
        round.DrawnNames.Add("李四");
        round.DrawnNames.Add("王五");

        var prepared = await fixture.Execution.PrepareAsync(new DrawExecutionRequest(
            TargetCount: 1,
            Filters: new DrawFilterOptions(),
            Mode: DrawMode.Weighted));
        Assert.Equal("张三", prepared.SelectedMembers[0].Name);
        await fixture.Execution.CommitAsync(prepared, prepared.SelectedMembers, confirmSelection: true);

        var outcome = await fixture.Execution.ReplaceAsync(prepared, prepared.SelectedMembers[0]);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Replacement);
        Assert.DoesNotContain(outcome.Replacement!.Name, prepared.SelectedMembers.Select(member => member.Name));
    }

    private sealed class DrawCommitFixture : IDisposable
    {
        private readonly string _overlayDirectory;

        private DrawCommitFixture(
            AppDataService data,
            TestSettingsService settings,
            DrawExecutionService execution,
            string overlayDirectory)
        {
            Data = data;
            Settings = settings;
            Execution = execution;
            _overlayDirectory = overlayDirectory;
        }

        public AppDataService Data { get; }
        public TestSettingsService Settings { get; }
        public DrawExecutionService Execution { get; }

        public static DrawCommitFixture Create()
        {
            var data = new AppDataService(new InMemoryStateStore(), new ConstantWeightProviderFactory());
            data.InitializeAsync().GetAwaiter().GetResult();
            data.ActiveClassroom.Members.Add(new Member { Name = "张三" });
            data.State.Settings.DrawMode = DrawMode.Weighted;
            data.State.Settings.DrawTargetCount = 1;
            var settings = new TestSettingsService(data);
            var overlayDirectory = Path.Combine(
                Path.GetTempPath(),
                "PickWeight.DrawCommit.Tests",
                Guid.NewGuid().ToString("N"));
            var execution = new DrawExecutionService(
                data,
                new DrawService(() => 0d),
                settings,
                new NotificationService(settings),
                new TestClassIslandNotificationBridge(),
                new ObsOverlayService(Path.Combine(overlayDirectory, "latest-draw.txt")),
                new AppLogService());
            return new DrawCommitFixture(data, settings, execution, overlayDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_overlayDirectory))
            {
                Directory.Delete(_overlayDirectory, recursive: true);
            }
        }
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

        public Task<Classroom> LoadClassroomAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_classrooms.TryGetValue(name, out var classroom) ? classroom.Clone() : new Classroom());

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

    private sealed class ConstantWeightProviderFactory : IImplicitWeightProviderFactory
    {
        public IImplicitWeightProvider Create(string classroomDirectory) => new ConstantWeightProvider();
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
}
