using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight.App.Tests;

/// <summary>
/// 小组页添加成员弹窗的 ViewModel 逻辑测试：空查询显示完整可勾选名单、
/// 搜索只做过滤、勾选不随搜索丢失、AddMembers 以勾选集合为准入组。
/// </summary>
public sealed class GroupsPageViewModelTests
{
    [Fact]
    public void EmptyQuery_ShowsAllAvailableMembers_ExcludingGroupMembers()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四", "王五", "赵六");
            data.ActiveClassroom.Groups.Add(new Group
            {
                GroupName = "第一组",
                Members = ["李四", "王五"]
            });
        });

        // 默认选中小组成员不重复，空查询直接展示「全部成员 - 当前组成员」的完整名单
        Assert.Equal(["李四", "王五"], viewModel.Groups[0].Members);
        Assert.Equal(["张三", "赵六"], viewModel.FilteredAvailableMembers);
        Assert.True(viewModel.HasAvailableMembers);
    }

    [Fact]
    public void Search_FiltersSameList_AndStillExcludesGroupMembers()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四", "王五");
            data.ActiveClassroom.Groups.Add(new Group
            {
                GroupName = "第一组",
                Members = ["李四"]
            });
        });

        viewModel.AvailableMemberSearchText = "张";
        Assert.Equal(["张三"], viewModel.FilteredAvailableMembers);

        // 命中的关键字是组内成员时，依然不能重复加入
        viewModel.AvailableMemberSearchText = "李";
        Assert.Empty(viewModel.FilteredAvailableMembers);

        // 搜索关键字大小写不敏感
        viewModel.AvailableMemberSearchText = "张";
        Assert.Equal(["张三"], viewModel.FilteredAvailableMembers);
    }

    [Fact]
    public void SearchThenClear_PreservesCheckedSelection()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四", "王五");
            data.ActiveClassroom.Groups.Add(new Group { GroupName = "第一组" });
        });

        viewModel.SelectedAvailableMembers.Add("张三");

        // 搜索后选中集合不丢失
        viewModel.AvailableMemberSearchText = "王";
        Assert.Equal(["王五"], viewModel.FilteredAvailableMembers);
        Assert.Contains("张三", viewModel.SelectedAvailableMembers);

        // 清空搜索回到完整名单，勾选依然保留
        viewModel.AvailableMemberSearchText = string.Empty;
        Assert.Equal(["张三", "李四", "王五"], viewModel.FilteredAvailableMembers);
        Assert.Contains("张三", viewModel.SelectedAvailableMembers);
    }

    [Fact]
    public void AddMembers_UsesSelectedSetAsSourceOfTruth()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四", "王五");
            data.ActiveClassroom.Groups.Add(new Group { GroupName = "第一组" });
        });

        viewModel.SelectedAvailableMembers.Add("张三");
        viewModel.SelectedAvailableMembers.Add("王五");
        viewModel.AddMembers();

        Assert.Equal(["张三", "王五"], viewModel.Groups[0].Members);
        // 已入组成员从可加入名单中移除
        Assert.Equal(["李四"], viewModel.FilteredAvailableMembers);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void AddMembers_SkipsAlreadyGroupedMembers_IgnoresCase()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "Alice", "Bob");
            data.ActiveClassroom.Groups.Add(new Group
            {
                GroupName = "第一组",
                Members = ["Alice"]
            });
        });

        // 已入组成员大小写不同也跳过；新成员忽略大小写入组
        viewModel.SelectedAvailableMembers.Add("alice");
        viewModel.SelectedAvailableMembers.Add("BOB");
        viewModel.AddMembers();

        Assert.Equal(["Alice", "BOB"], viewModel.Groups[0].Members);
        Assert.True(viewModel.IsDirty);
        // 组内成员（含大小写变体）不会出现在可加入名单
        Assert.Empty(viewModel.FilteredAvailableMembers);
    }

    [Fact]
    public void AddMembers_WithExplicitList_StillWorks()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四");
            data.ActiveClassroom.Groups.Add(new Group { GroupName = "第一组" });
        });

        viewModel.AddMembers(["赵六", "赵六", "  "]);

        Assert.Equal(["赵六"], viewModel.Groups[0].Members);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void Refresh_RaisesAvailableMembersRefreshed_ForSelectionRestore()
    {
        var viewModel = CreateViewModel(data =>
        {
            AddMembers(data, "张三", "李四");
            data.ActiveClassroom.Groups.Add(new Group { GroupName = "第一组" });
        });

        var fired = 0;
        viewModel.AvailableMembersRefreshed += () => fired++;

        viewModel.AvailableMemberSearchText = "张";
        viewModel.AvailableMemberSearchText = string.Empty;
        viewModel.AddMembers();

        Assert.Equal(3, fired);
    }

    private static GroupsPageViewModel CreateViewModel(Action<AppDataService>? arrange = null)
    {
        var data = new AppDataService(new InMemoryStateStore(), new ConstantWeightProviderFactory());
        data.InitializeAsync().GetAwaiter().GetResult();
        arrange?.Invoke(data);
        return new GroupsPageViewModel(data, new FileDialogService(), new GroupImportExportService());
    }

    private static void AddMembers(AppDataService data, params string[] names)
    {
        foreach (var name in names)
        {
            data.ActiveClassroom.Members.Add(new Member { Name = name });
        }
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        private AppState _state = new();
        private readonly Dictionary<string, Classroom> _classrooms = new(StringComparer.OrdinalIgnoreCase);

        public Task<AppState> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_state.Clone());

        public Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
        {
            _state = state.Clone();
            return Task.CompletedTask;
        }

        public Task<Classroom> LoadClassroomAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_classrooms.TryGetValue(name, out var stored)
                ? stored.Clone()
                : new Classroom { Name = name });
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
    }

    private sealed class ConstantWeightProviderFactory : IImplicitWeightProviderFactory
    {
        public IImplicitWeightProvider Create(string classroomDirectory) =>
            new ConstantWeightProvider();
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
}
