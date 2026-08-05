using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight_App.Services;

/// <summary>
/// 应用内共享状态的唯一所有者。页面编辑器操作草稿，只有保存时才替换这里的正式状态。
/// </summary>
public sealed class AppDataService
{
    private readonly IStateStore _stateStore;
    private readonly IImplicitWeightProvider _weightProvider;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public AppState State { get; private set; } = new();
    public bool IsFirstRunRequired => !State.Settings.IsFirstRunCompleted;
    public string StartupNotice { get; private set; } = string.Empty;
    public event EventHandler? MembersChanged;
    public event EventHandler? GroupsChanged;

    public AppDataService(IStateStore stateStore, IImplicitWeightProvider weightProvider)
    {
        _stateStore = stateStore;
        _weightProvider = weightProvider;
    }

    public async Task InitializeAsync()
    {
        State = await _stateStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(State.Settings.LocalApiToken))
        {
            State.Settings.LocalApiToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            await _stateStore.SaveAsync(State);
        }
        await ReloadImplicitWeightsAsync();
    }

    public async Task ReloadImplicitWeightsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _weightProvider.SynchronizeAsync(State.Members, cancellationToken: cancellationToken);
        foreach (var member in State.Members)
        {
            member.ImplicitWeight = result.Weights.TryGetValue(member.Name.Trim(), out var value) ? value : 1d;
        }
        StartupNotice = string.Empty;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
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
        State.Members = members.Select(member => member.Clone()).ToList();

        var validNames = new HashSet<string>(State.Members.Select(member => member.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var group in State.Groups)
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

        await ReloadImplicitWeightsAfterMemberChangeAsync(renames);
        await SaveAsync();
        MembersChanged?.Invoke(this, EventArgs.Empty);
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReplaceGroupsAsync(IEnumerable<Group> groups)
    {
        State.Groups = groups.Select(group => group.Clone()).ToList();
        await SaveAsync();
        GroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadImplicitWeightsAfterMemberChangeAsync(IReadOnlyDictionary<string, string>? renames)
    {
        var result = await _weightProvider.SynchronizeAsync(State.Members, renames);
        foreach (var member in State.Members)
        {
            member.ImplicitWeight = result.Weights.TryGetValue(member.Name.Trim(), out var value) ? value : 1d;
        }
    }
}
