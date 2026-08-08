using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight_App.Services;

/// <summary>
/// 应用内共享状态的唯一所有者。页面编辑器操作草稿，只有保存时才替换这里的正式状态。
/// 全局设置存于 <see cref="State"/>，名单、小组、历史等按班级存于 <see cref="ActiveClassroom"/>。
/// </summary>
public sealed class AppDataService
{
    private readonly IStateStore _stateStore;
    private readonly IImplicitWeightProviderFactory _weightProviderFactory;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private IImplicitWeightProvider _weightProvider = null!;

    public AppState State { get; private set; } = new();

    /// <summary>当前激活的班级。名单、小组、历史、预设、座位表都取自这里。</summary>
    public Classroom ActiveClassroom { get; private set; } = new();

    public bool IsFirstRunRequired => !State.Settings.IsFirstRunCompleted;
    public string StartupNotice { get; private set; } = string.Empty;

    public event EventHandler? MembersChanged;
    public event EventHandler? GroupsChanged;

    /// <summary>启动归档或手动归档完成后触发，统计页据此刷新。</summary>
    public event EventHandler? HistoryArchived;

    /// <summary>切换、新建、重命名或删除班级后触发，页面需整体刷新数据源。</summary>
    public event EventHandler? ActiveClassroomChanged;

    /// <summary>班级列表发生增删改后触发，用于刷新班级切换下拉。</summary>
    public event EventHandler? ClassroomListChanged;

    public AppDataService(IStateStore stateStore, IImplicitWeightProviderFactory weightProviderFactory)
    {
        _stateStore = stateStore;
        _weightProviderFactory = weightProviderFactory;
    }

    public async Task InitializeAsync()
    {
        State = await _stateStore.LoadAsync();
        StartupNotice = State.StartupNotice;

        if (string.IsNullOrWhiteSpace(State.Settings.LocalApiToken))
        {
            State.Settings.LocalApiToken = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            if (!State.IsReadOnly)
            {
                await _stateStore.SaveAsync(State);
            }
        }

        await LoadActiveClassroomAsync();

        // 启动时归档一次超期历史，保证主文件只保留近期记录
        try
        {
            await ArchiveHistoryAsync(CancellationToken.None);
        }
        catch
        {
            // 归档失败不影响启动，下次启动或手动归档会重试
        }
    }

    private async Task LoadActiveClassroomAsync(CancellationToken cancellationToken = default)
    {
        ActiveClassroom = await _stateStore.LoadClassroomAsync(State.ActiveClassroomName, cancellationToken);
        _weightProvider = _weightProviderFactory.Create(
            _stateStore.GetClassroomDirectory(State.ActiveClassroomName));
        await ReloadImplicitWeightsAsync(cancellationToken);
    }

    public async Task ReloadImplicitWeightsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _weightProvider.SynchronizeAsync(
            ActiveClassroom.Members,
            cancellationToken: cancellationToken);
        ApplyImplicitWeights(result);
    }

    private void ApplyImplicitWeights(ImplicitWeightLoadResult result)
    {
        foreach (var member in ActiveClassroom.Members)
        {
            member.ImplicitWeight = result.Weights.TryGetValue(member.Name.Trim(), out var value) ? value : 1d;
        }
    }

    /// <summary>清除启动提示，用户已看过即可调用。</summary>
    public void ClearStartupNotice()
    {
        StartupNotice = string.Empty;
        State.StartupNotice = string.Empty;
    }

    /// <summary>保存全局设置与当前班级数据。</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _stateStore.SaveAsync(State, cancellationToken);
            await _stateStore.SaveClassroomAsync(ActiveClassroom, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// 把当前班级抽取历史中超期条目移入 archive/{yyyyMM}.json，
    /// 主文件只保留最近 <see cref="AppSettings.HistoryRetentionDays"/> 天的记录。
    /// 已归档的历史可通过 <see cref="LoadArchivedHistoryAsync"/> 读回。
    /// </summary>
    public async Task ArchiveHistoryAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Max(1, State.Settings.HistoryRetentionDays);
        var cutoffUtc = DateTimeOffset.Now.AddDays(-retentionDays).UtcDateTime;
        var archive = new HistoryArchiveService(_stateStore.GetClassroomDirectory(ActiveClassroom.Name));
        var archived = await archive.ArchiveOlderThanAsync(ActiveClassroom.DrawHistory, cutoffUtc, cancellationToken);
        if (archived.Count == 0)
        {
            return;
        }

        await SaveAsync(cancellationToken);
        HistoryArchived?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>读取当前班级的全部归档历史（含损坏文件的容错）。</summary>
    public Task<IReadOnlyList<DrawHistoryEntry>> LoadArchivedHistoryAsync(CancellationToken cancellationToken = default)
    {
        var archive = new HistoryArchiveService(_stateStore.GetClassroomDirectory(ActiveClassroom.Name));
        return archive.LoadAllAsync(cancellationToken);
    }

    /// <summary>只保存全局设置，不触碰班级文件。</summary>
    public async Task SaveSettingsOnlyAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _stateStore.SaveAsync(State, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task MarkFirstRunCompletedAsync()
    {
        State.Settings.IsFirstRunCompleted = true;
        await SaveAsync();
    }

    public async Task ResetFirstRunAsync()
    {
        State.Settings.IsFirstRunCompleted = false;
        await SaveAsync();
    }

    public async Task ReplaceMembersAsync(
        IEnumerable<Member> members,
        IReadOnlyDictionary<string, string>? renames = null)
    {
        ActiveClassroom.Members = members.Select(member => member.Clone()).ToList();

        var validNames = new HashSet<string>(
            ActiveClassroom.Members.Select(member => member.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in ActiveClassroom.Groups)
        {
            if (renames is not null)
            {
                for (var index = 0; index < group.Members.Count; index++)
                {
                    var oldName = group.Members[index];
                    if (renames.TryGetValue(oldName, out var newName))
                    {
                        group.Members[index] = newName;
                    }
                }

                group.Members = group.Members
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            group.Members.RemoveAll(name => !validNames.Contains(name));
        }

        SynchronizeSeatMap(validNames, renames);

        var result = await _weightProvider.SynchronizeAsync(ActiveClassroom.Members, renames);
        ApplyImplicitWeights(result);

        await SaveAsync();
        MembersChanged?.Invoke(this, EventArgs.Empty);
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>成员改名或删除后修正座位表引用，避免座位指向不存在的成员。</summary>
    private void SynchronizeSeatMap(
        HashSet<string> validNames,
        IReadOnlyDictionary<string, string>? renames)
    {
        if (ActiveClassroom.SeatMap is null)
        {
            return;
        }

        foreach (var column in ActiveClassroom.SeatMap.Columns)
        {
            for (var index = 0; index < column.MemberNames.Count; index++)
            {
                var name = column.MemberNames[index];
                if (name is null)
                {
                    continue;
                }

                if (renames is not null && renames.TryGetValue(name, out var newName))
                {
                    name = newName;
                    column.MemberNames[index] = newName;
                }

                if (!validNames.Contains(name))
                {
                    column.MemberNames[index] = null;
                }
            }
        }
    }

    public async Task ReplaceGroupsAsync(IEnumerable<Group> groups)
    {
        ActiveClassroom.Groups = groups.Select(group => group.Clone()).ToList();
        await SaveAsync();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- 班级管理 ----------

    public IReadOnlyList<string> ClassroomNames => State.ClassroomNames;

    /// <summary>班级列表项，供管理对话框与切换下拉显示。</summary>
    public sealed record ClassroomSummary(string Name, string? Subject, int MemberCount);

    /// <summary>加载所有班级的轻量摘要（名称、科目、人数），用于下拉与列表展示。</summary>
    public async Task<IReadOnlyList<ClassroomSummary>> ListClassroomSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<ClassroomSummary>();
        foreach (var name in State.ClassroomNames)
        {
            try
            {
                var classroom = await _stateStore.LoadClassroomAsync(name, cancellationToken);
                summaries.Add(new ClassroomSummary(classroom.Name, classroom.Subject, classroom.Members.Count));
            }
            catch
            {
                summaries.Add(new ClassroomSummary(name, null, 0));
            }
        }

        return summaries;
    }

    /// <summary>更新班级科目。科目仅用于历史记录标注，不影响任何抽取规则。</summary>
    public async Task UpdateClassroomSubjectAsync(
        string name,
        string? subject,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (!State.ClassroomNames.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"班级“{trimmed}”不存在。");
        }

        var classroom = await _stateStore.LoadClassroomAsync(trimmed, cancellationToken);
        classroom.Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        await _stateStore.SaveClassroomAsync(classroom, cancellationToken);

        if (string.Equals(trimmed, State.ActiveClassroomName, StringComparison.Ordinal))
        {
            ActiveClassroom.Subject = classroom.Subject;
        }

        ClassroomListChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>切换激活班级。切换前会先保存当前班级，避免丢失未落盘的改动。</summary>
    public async Task SwitchClassroomAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (string.Equals(trimmed, State.ActiveClassroomName, StringComparison.Ordinal))
        {
            return;
        }

        if (!State.ClassroomNames.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"班级“{trimmed}”不存在。");
        }

        await SaveAsync(cancellationToken);

        State.ActiveClassroomName = trimmed;
        await LoadActiveClassroomAsync(cancellationToken);
        await SaveSettingsOnlyAsync(cancellationToken);

        RaiseClassroomSwitched();
    }

    /// <summary>新建班级并立即切换过去。名称非法或重名时抛出，调用方负责显示提示。</summary>
    public async Task CreateClassroomAsync(
        string name,
        IEnumerable<Member>? initialMembers = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        var error = ClassroomNameValidator.Validate(trimmed)
            ?? ClassroomNameValidator.ValidateUnique(trimmed, State.ClassroomNames);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        var classroom = new Classroom { Name = trimmed };
        if (initialMembers is not null)
        {
            classroom.Members = initialMembers.Select(member => member.Clone()).ToList();
        }

        await SaveAsync(cancellationToken);
        await _stateStore.CreateClassroomAsync(classroom, cancellationToken);

        State.ClassroomNames = (await _stateStore.ListClassroomNamesAsync(cancellationToken)).ToList();
        State.ActiveClassroomName = trimmed;
        await LoadActiveClassroomAsync(cancellationToken);
        await SaveSettingsOnlyAsync(cancellationToken);

        ClassroomListChanged?.Invoke(this, EventArgs.Empty);
        RaiseClassroomSwitched();
    }

    /// <summary>重命名班级。文件夹名即班级名，因此这里会移动整个文件夹。</summary>
    public async Task RenameClassroomAsync(
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var trimmedOld = oldName.Trim();
        var trimmedNew = newName.Trim();
        if (string.Equals(trimmedOld, trimmedNew, StringComparison.Ordinal))
        {
            return;
        }

        var others = State.ClassroomNames
            .Where(item => !string.Equals(item, trimmedOld, StringComparison.Ordinal))
            .ToList();
        var error = ClassroomNameValidator.Validate(trimmedNew)
            ?? ClassroomNameValidator.ValidateUnique(trimmedNew, others);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        var isActive = string.Equals(trimmedOld, State.ActiveClassroomName, StringComparison.Ordinal);
        if (isActive)
        {
            await SaveAsync(cancellationToken);
        }

        var oldDirectory = _stateStore.GetClassroomDirectory(trimmedOld);
        await _stateStore.RenameClassroomAsync(trimmedOld, trimmedNew, cancellationToken);
        (_weightProviderFactory as JsonImplicitWeightProviderFactory)?.Invalidate(oldDirectory);

        State.ClassroomNames = (await _stateStore.ListClassroomNamesAsync(cancellationToken)).ToList();
        if (isActive)
        {
            State.ActiveClassroomName = trimmedNew;
            await LoadActiveClassroomAsync(cancellationToken);
        }

        await SaveSettingsOnlyAsync(cancellationToken);

        ClassroomListChanged?.Invoke(this, EventArgs.Empty);
        if (isActive)
        {
            RaiseClassroomSwitched();
        }
    }

    /// <summary>删除班级。至少要保留一个班级，删除当前班级后自动切到列表中的第一个。</summary>
    public async Task DeleteClassroomAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (State.ClassroomNames.Count <= 1)
        {
            throw new InvalidOperationException("至少需要保留一个班级，无法删除最后一个班级。");
        }

        if (!State.ClassroomNames.Contains(trimmed, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"班级“{trimmed}”不存在。");
        }

        var directory = _stateStore.GetClassroomDirectory(trimmed);
        await _stateStore.DeleteClassroomAsync(trimmed, cancellationToken);
        (_weightProviderFactory as JsonImplicitWeightProviderFactory)?.Invalidate(directory);

        State.ClassroomNames = (await _stateStore.ListClassroomNamesAsync(cancellationToken)).ToList();

        var wasActive = string.Equals(trimmed, State.ActiveClassroomName, StringComparison.Ordinal);
        if (wasActive)
        {
            State.ActiveClassroomName = State.ClassroomNames[0];
            await LoadActiveClassroomAsync(cancellationToken);
        }

        await SaveSettingsOnlyAsync(cancellationToken);

        ClassroomListChanged?.Invoke(this, EventArgs.Empty);
        if (wasActive)
        {
            RaiseClassroomSwitched();
        }
    }

    private void RaiseClassroomSwitched()
    {
        ActiveClassroomChanged?.Invoke(this, EventArgs.Empty);
        MembersChanged?.Invoke(this, EventArgs.Empty);
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }
}
