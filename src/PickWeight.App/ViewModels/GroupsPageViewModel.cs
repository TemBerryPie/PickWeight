using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;

namespace PickWeight_App.ViewModels;

public partial class GroupsPageViewModel : ObservableObject, IEditablePageViewModel
{
    private readonly AppDataService _data;
    private readonly FileDialogService _files;
    private readonly IGroupImportExportService _importExport;

    public ObservableCollection<GroupEditorViewModel> Groups { get; } = [];
    public ObservableCollection<string> AllMembers { get; } = [];
    public ObservableCollection<string> FilteredAvailableMembers { get; } = [];

    /// <summary>弹窗中已勾选的待加入成员；搜索过滤不丢失，以忽略大小写的方式去重。</summary>
    public HashSet<string> SelectedAvailableMembers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>可加入成员列表重填完成后触发，页面据此恢复 ListView 的勾选状态。</summary>
    public event Action? AvailableMembersRefreshed;

    [ObservableProperty] private GroupEditorViewModel? selectedGroup;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity statusSeverity = InfoBarSeverity.Success;
    [ObservableProperty] private string availableMemberSearchText = string.Empty;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasGroups => Groups.Count > 0;
    public bool HasSelectedGroup => SelectedGroup is not null;
    public bool HasAvailableMembers => FilteredAvailableMembers.Count > 0;
    public bool HasAvailableMemberSearchQuery => !string.IsNullOrWhiteSpace(AvailableMemberSearchText);
    public bool HasGroupMembers => SelectedGroup?.Members.Count > 0;
    public Visibility GroupEditorVisibility => HasSelectedGroup ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoGroupsVisibility => HasGroups ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NoGroupMembersVisibility => HasGroupMembers ? Visibility.Collapsed : Visibility.Visible;

    public GroupsPageViewModel(
        AppDataService data,
        FileDialogService files,
        IGroupImportExportService importExport)
    {
        _data = data;
        _files = files;
        _importExport = importExport;
        Reload();
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnSelectedGroupChanged(GroupEditorViewModel? value)
    {
        RefreshMemberSearch();
        NotifyCollectionStateChanged();
    }

    partial void OnAvailableMemberSearchTextChanged(string value) => RefreshMemberSearch();

    public void Reload()
    {
        Groups.Clear();
        AllMembers.Clear();
        foreach (var member in _data.ActiveClassroom.Members
            .Select(member => member.Name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            AllMembers.Add(member);
        }

        foreach (var group in _data.ActiveClassroom.Groups)
        {
            Groups.Add(new GroupEditorViewModel(group.Clone(), MarkDirty));
        }

        SelectedGroup = Groups.FirstOrDefault();
        AvailableMemberSearchText = string.Empty;
        RefreshMemberSearch();
        IsDirty = false;
        NotifyCollectionStateChanged();
    }

    public async Task SaveAsync()
    {
        var rawModels = Groups.Select(group => group.ToModel(clampMultiplier: false)).ToList();
        var validation = GroupRules.ValidateGroups(rawModels, _data.ActiveClassroom.Members);
        if (!validation.IsValid)
        {
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = validation.Errors[0];
            return;
        }

        try
        {
            await _data.ReplaceGroupsAsync(Groups.Select(group => group.ToModel()));
            IsDirty = false;
            StatusSeverity = validation.Warnings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            StatusMessage = validation.Warnings.Count > 0
                ? $"小组数据已保存。{validation.Warnings[0]}"
                : "小组数据已保存。";
        }
        catch (Exception ex)
        {
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = $"小组数据保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void CreateGroup()
    {
        var group = new GroupEditorViewModel(new Group { GroupName = $"新小组 {Groups.Count + 1}" }, MarkDirty);
        Groups.Add(group);
        SelectedGroup = group;
        MarkDirty();
        NotifyCollectionStateChanged();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        Groups.Remove(SelectedGroup);
        SelectedGroup = Groups.FirstOrDefault();
        RefreshMemberSearch();
        MarkDirty();
        NotifyCollectionStateChanged();
    }

    public void ResetAvailableMemberSearch() => AvailableMemberSearchText = string.Empty;

    public void AddMembers(IEnumerable<string>? selectedMembers = null)
    {
        if (SelectedGroup is null)
        {
            StatusSeverity = InfoBarSeverity.Informational;
            StatusMessage = "请先选择小组。";
            return;
        }

        var selected = (selectedMembers ?? SelectedAvailableMembers)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var added = 0;
        foreach (var name in selected)
        {
            if (SelectedGroup.Members.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            SelectedGroup.Members.Add(name);
            added++;
        }

        RefreshMemberSearch();
        if (added == 0)
        {
            StatusSeverity = InfoBarSeverity.Informational;
            StatusMessage = "没有可加入的新成员。";
            return;
        }

        MarkDirty();
        StatusSeverity = InfoBarSeverity.Success;
        StatusMessage = $"已加入 {added} 名成员；点击“保存”后生效。";
    }

    public void RemoveMember(string? name)
    {
        if (SelectedGroup is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!SelectedGroup.Members.Remove(name))
        {
            return;
        }

        RefreshMemberSearch();
        MarkDirty();
        StatusSeverity = InfoBarSeverity.Success;
        StatusMessage = $"已将 {name} 移出小组；点击“保存”后生效。";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var content = await _files.OpenTextAsync(".json");
        if (content is null)
        {
            StatusSeverity = InfoBarSeverity.Informational;
            StatusMessage = "已取消导入。";
            return;
        }

        var models = Groups.Select(group => group.ToModel()).ToList();
        ImportResult result;
        try
        {
            result = _importExport.Import(content, models, _data.ActiveClassroom.Members);
        }
        catch (InvalidDataException ex)
        {
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = ex.Message;
            return;
        }

        Groups.Clear();
        foreach (var group in models)
        {
            Groups.Add(new GroupEditorViewModel(group, MarkDirty));
        }

        SelectedGroup = Groups.FirstOrDefault();
        RefreshMemberSearch();
        MarkDirty();
        NotifyCollectionStateChanged();
        StatusSeverity = InfoBarSeverity.Success;
        StatusMessage = $"新增 {result.AddedCount} 组，合并 {result.MergedCount} 组，跳过 {result.SkippedCount} 项；点击“保存”后生效。";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        await _files.SaveTextAsync("PickWeight-小组", ".json", _importExport.Export(Groups.Select(group => group.ToModel())));
        StatusSeverity = InfoBarSeverity.Success;
        StatusMessage = "导出操作已完成。";
    }

    [RelayCommand]
    private Task SaveCommandAsync() => SaveAsync();

    private void MarkDirty() => IsDirty = true;

    private void RefreshMemberSearch()
    {
        FilteredAvailableMembers.Clear();
        if (SelectedGroup is null)
        {
            NotifyAvailableMemberSearchState();
            AvailableMembersRefreshed?.Invoke();
            return;
        }

        var query = AvailableMemberSearchText.Trim();
        var groupMembers = SelectedGroup.Members.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var member in AllMembers
            .Where(name => !groupMembers.Contains(name))
            .Where(name => query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            FilteredAvailableMembers.Add(member);
        }

        NotifyAvailableMemberSearchState();
        AvailableMembersRefreshed?.Invoke();
    }

    private void NotifyAvailableMemberSearchState()
    {
        OnPropertyChanged(nameof(HasAvailableMembers));
        OnPropertyChanged(nameof(HasAvailableMemberSearchQuery));
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasGroupMembers));
        OnPropertyChanged(nameof(GroupEditorVisibility));
        OnPropertyChanged(nameof(NoGroupsVisibility));
        OnPropertyChanged(nameof(NoGroupMembersVisibility));
        NotifyAvailableMemberSearchState();
    }
}
