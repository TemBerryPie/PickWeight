using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;

namespace PickWeight_App.ViewModels;

/// <summary>
/// Presentation state for the primary draw page.  Rule edits are deliberately
/// staged in <see cref="RuleDialogDraft"/> so Cancel never changes the current
/// draw configuration.
/// </summary>
public partial class DrawPageViewModel : ObservableObject
{
    private readonly AppDataService _data;
    private readonly IDrawService _drawService;
    private readonly IDrawExecutionService _execution;
    private readonly ISettingsService _settings;
    private int _previewRefreshVersion;
    private bool _synchronizingGroups;
    private PreparedDraw? _currentDraw;

    public ObservableCollection<DrawResultItemViewModel> Results { get; } = [];
    public ObservableCollection<DrawProbabilityPreviewRowViewModel> ProbabilityPreview { get; } = [];
    public ObservableCollection<RuleSummaryRowViewModel> RuleSummaryRows { get; } = [];
    public ObservableCollection<string> GroupFilterOptions { get; } = [];
    public ObservableCollection<DrawPreset> DrawPresets { get; } = [];

    public event Action? DrawStarted;
    public event Action? DrawCompleted;

    [ObservableProperty]
    private int selectedDrawModeIndex;

    [ObservableProperty]
    private double targetCount = 1d;

    [ObservableProperty]
    private int includeGroupFilterIndex;

    [ObservableProperty]
    private int excludeGroupFilterIndex;

    [ObservableProperty]
    private double excludeRecentDrawCount;

    [ObservableProperty]
    private bool onlyNeverDrawn;

    [ObservableProperty]
    private string statusMessage = "准备开始抽取";

    [ObservableProperty]
    private string candidateSummary = "候选人：0";

    [ObservableProperty]
    private RuleDialogDraft? ruleDraft;

    [ObservableProperty]
    private DrawPreset? selectedDrawPreset;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanDiscardCurrentDraw => Results.Count > 0;
    public bool HasProbabilityPreview => ProbabilityPreview.Count > 0;
    public Visibility ProbabilityPreviewVisibility => HasProbabilityPreview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyProbabilityPreviewVisibility => HasProbabilityPreview ? Visibility.Collapsed : Visibility.Visible;
    public string ProbabilityPreviewEmptyText => "当前规则没有可抽取候选人。";
    public bool CanClearDrawFilters =>
        IncludeGroupFilterIndex > 0 ||
        ExcludeGroupFilterIndex > 0 ||
        ExcludeRecentDrawCount >= 1d ||
        OnlyNeverDrawn;

    public DrawPageViewModel(
        AppDataService data,
        IDrawService drawService,
        IDrawExecutionService execution,
        ISettingsService settings)
    {
        _data = data;
        _drawService = drawService;
        _execution = execution;
        _settings = settings;

        RebuildGroupFilterOptions();
        RebuildPresets();
        LoadRulesFromSettings();

        _settings.Changed += (_, args) =>
        {
            if (args.DrawRulesChanged)
            {
                LoadRulesFromSettings();
                _ = RefreshProbabilityPreviewAsync();
            }
        };
        _data.GroupsChanged += (_, _) => _ = HandleGroupsChangedAsync();
        _data.MembersChanged += (_, _) => _ = RefreshProbabilityPreviewAsync();

        _ = RefreshProbabilityPreviewAsync();
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnTargetCountChanged(double value) => RefreshRuleSummary();
    partial void OnIncludeGroupFilterIndexChanged(int value) => RefreshFilters();
    partial void OnExcludeGroupFilterIndexChanged(int value) => RefreshFilters();
    partial void OnExcludeRecentDrawCountChanged(double value) => RefreshFilters();
    partial void OnOnlyNeverDrawnChanged(bool value) => RefreshFilters();

    [RelayCommand]
    private async Task DrawAsync()
    {
        var prepared = await _execution.PrepareAsync(new DrawExecutionRequest(
            NormalizeTargetCount(),
            CurrentFilterOptions(),
            _settings.Current.DrawMode));
        if (prepared.SelectedMembers.Count == 0)
        {
            Results.Clear();
            _currentDraw = null;
            NotifyDrawStateChanged();
            StatusMessage = prepared.Result.Message ?? "当前规则没有可抽取候选人。";
            await RefreshProbabilityPreviewAsync(reloadWeights: false);
            return;
        }

        DrawStarted?.Invoke();
        Results.Clear();
        _currentDraw = prepared;
        var isManualSelection = prepared.DrawMode == DrawMode.Selection;
        var weights = prepared.Result.SelectedWeights ?? CreateWeightRows(prepared.SelectedMembers);
        for (var index = 0; index < prepared.SelectedMembers.Count; index++)
        {
            var member = prepared.SelectedMembers[index];
            var weightsForMember = index < weights.Count
                ? weights[index]
                : new DrawnMemberWeight(member.Name, member.ExplicitWeight, MemberRules.CalledWeight(member.CalledCount));
            Results.Add(new DrawResultItemViewModel(
                member,
                weightsForMember,
                prepared.DrawMode,
                isCommitted: !isManualSelection,
                allowsManualCommit: isManualSelection,
                CommitResultAsync,
                DiscardResultAsync));
        }

        DrawCompleted?.Invoke();
        if (!isManualSelection)
        {
            var outcome = await _execution.CommitAsync(prepared);
            StatusMessage = CreateCompletionMessage(prepared.Result, outcome);
        }
        else
        {
            StatusMessage = "请选择要确认的结果；确认后会记录历史并更新被点次数。";
        }

        NotifyDrawStateChanged();
        await RefreshProbabilityPreviewAsync(reloadWeights: false);
    }

    public void BeginRuleEdit()
    {
        RuleDraft = new RuleDialogDraft(
            this,
            SelectedDrawModeIndex,
            TargetCount,
            IncludeGroupFilterIndex,
            ExcludeGroupFilterIndex,
            ExcludeRecentDrawCount,
            OnlyNeverDrawn,
            GroupFilterOptions);
        _ = RefreshRuleDraftAsync(RuleDraft);
    }

    public async Task ApplyRuleDraftAsync()
    {
        if (RuleDraft is not { } draft)
        {
            return;
        }

        var mode = (DrawMode)Math.Clamp(draft.SelectedDrawModeIndex, 0, 3);
        var targetCount = NormalizeTargetCount(draft.TargetCount);
        var include = GroupNameFromDraftIndex(draft.IncludeGroupFilterIndex, draft.GroupFilterOptions);
        var exclude = GroupNameFromDraftIndex(draft.ExcludeGroupFilterIndex, draft.GroupFilterOptions);
        var recentCount = NormalizeRecentDrawCount(draft.ExcludeRecentDrawCount);
        await _settings.UpdateAsync(settings =>
        {
            settings.DrawMode = mode;
            settings.DrawTargetCount = targetCount;
            settings.DrawIncludeGroupName = include;
            settings.DrawExcludeGroupName = exclude;
            settings.DrawExcludeRecentDrawCount = recentCount;
            settings.DrawOnlyNeverDrawn = draft.OnlyNeverDrawn;
        });

        LoadRulesFromSettings();
        RuleDraft = null;
        await RefreshProbabilityPreviewAsync();
    }

    public void CancelRuleEdit() => RuleDraft = null;

    public async Task SavePresetAsync(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("预设名称不能为空。", nameof(name));
        }

        var settings = _settings.Current;
        var preset = new DrawPreset
        {
            Name = normalizedName,
            DrawMode = settings.DrawMode,
            TargetCount = settings.DrawTargetCount,
            IncludeGroupName = settings.DrawIncludeGroupName,
            ExcludeGroupName = settings.DrawExcludeGroupName,
            ExcludeRecentDrawCount = settings.DrawExcludeRecentDrawCount,
            OnlyNeverDrawn = settings.DrawOnlyNeverDrawn
        };
        var existing = _data.State.DrawPresets.FindIndex(item => string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _data.State.DrawPresets[existing] = preset;
        else _data.State.DrawPresets.Add(preset);
        await _data.SaveAsync();
        RebuildPresets(normalizedName);
        StatusMessage = $"已保存预设“{normalizedName}”。";
    }

    public async Task ApplySelectedPresetAsync()
    {
        if (SelectedDrawPreset is null) return;
        var preset = SelectedDrawPreset;
        await _settings.UpdateAsync(settings =>
        {
            settings.DrawMode = preset.DrawMode;
            settings.DrawTargetCount = Math.Max(1, preset.TargetCount);
            settings.DrawIncludeGroupName = preset.IncludeGroupName;
            settings.DrawExcludeGroupName = preset.ExcludeGroupName;
            settings.DrawExcludeRecentDrawCount = Math.Max(0, preset.ExcludeRecentDrawCount);
            settings.DrawOnlyNeverDrawn = preset.OnlyNeverDrawn;
        });
        LoadRulesFromSettings();
        await RefreshProbabilityPreviewAsync();
        StatusMessage = $"已应用预设“{preset.Name}”。";
    }

    public async Task DeleteSelectedPresetAsync()
    {
        if (SelectedDrawPreset is null) return;
        var name = SelectedDrawPreset.Name;
        _data.State.DrawPresets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        await _data.SaveAsync();
        RebuildPresets();
        StatusMessage = $"已删除预设“{name}”。";
    }

    internal void ScheduleRuleDraftRefresh(RuleDialogDraft draft)
    {
        if (ReferenceEquals(RuleDraft, draft))
        {
            _ = RefreshRuleDraftAsync(draft);
        }
    }

    private async Task CommitResultAsync(DrawResultItemViewModel item)
    {
        if (_currentDraw is null || !item.CanCommitResult)
        {
            return;
        }

        var isFinalConfirmation = Results
            .Where(row => !row.IsDiscarded)
            .All(row => ReferenceEquals(row, item) || row.IsCommitted);
        IReadOnlyList<Member> membersToCommit = isFinalConfirmation
            ? Results.Where(row => !row.IsDiscarded).Select(row => row.Member).ToList()
            : [item.Member];
        var outcome = await _execution.CommitAsync(
            _currentDraw,
            membersToCommit,
            confirmSelection: true,
            notify: isFinalConfirmation);
        item.MarkCommitted();
        StatusMessage = outcome.WasAutoReset
            ? $"{item.Name} 已确认，已自动重置权重。"
            : $"{item.Name} 已确认。";
        if (!outcome.ClassIslandSucceeded)
        {
            StatusMessage += " 通知发送失败。";
        }

        await RefreshProbabilityPreviewAsync(reloadWeights: false);
    }

    private async Task DiscardResultAsync(DrawResultItemViewModel item)
    {
        if (_currentDraw is null || item.IsDiscarded)
        {
            return;
        }

        item.MarkDiscarded();
        Results.Remove(item);
        var committed = Results.Where(row => row.IsCommitted && !row.IsDiscarded).Select(row => row.Member).ToList();
        await _execution.RebuildCommittedStateAsync(_currentDraw, committed);
        if (Results.Count == 0)
        {
            _currentDraw = null;
        }

        NotifyDrawStateChanged();
        StatusMessage = $"{item.Name} 已丢弃。";
        await RefreshProbabilityPreviewAsync(reloadWeights: false);
    }

    [RelayCommand(CanExecute = nameof(CanDiscardCurrentDraw))]
    private async Task DiscardCurrentDrawAsync()
    {
        if (_currentDraw is null)
        {
            return;
        }

        await _execution.DiscardAsync(_currentDraw);
        foreach (var item in Results)
        {
            item.MarkDiscarded();
        }

        Results.Clear();
        _currentDraw = null;
        NotifyDrawStateChanged();
        StatusMessage = "本次抽取已丢弃。";
        await RefreshProbabilityPreviewAsync(reloadWeights: false);
    }

    [RelayCommand(CanExecute = nameof(CanClearDrawFilters))]
    private void ClearDrawFilters()
    {
        IncludeGroupFilterIndex = 0;
        ExcludeGroupFilterIndex = 0;
        ExcludeRecentDrawCount = 0d;
        OnlyNeverDrawn = false;
        RefreshFilters();
    }

    private void RefreshFilters()
    {
        if (_synchronizingGroups)
        {
            return;
        }

        NotifyFilterStateChanged();
        _ = RefreshProbabilityPreviewAsync();
    }

    private async Task RefreshProbabilityPreviewAsync(bool reloadWeights = true)
    {
        var version = ++_previewRefreshVersion;
        if (reloadWeights)
        {
            await _data.ReloadImplicitWeightsAsync();
        }

        var filtered = GetFilteredMembers(CurrentFilterOptions());
        var preview = _drawService.PreviewProbabilities(
            filtered,
            _data.State.Groups,
            new DrawOptions(_settings.Current.DrawMode));
        if (version != _previewRefreshVersion)
        {
            return;
        }

        UpdateProbabilityPreview(ProbabilityPreview, preview);
        CandidateSummary = $"筛选候选 {filtered.Count} 人，可抽取 {preview.Count} 人";
        OnPropertyChanged(nameof(HasProbabilityPreview));
        OnPropertyChanged(nameof(ProbabilityPreviewVisibility));
        OnPropertyChanged(nameof(EmptyProbabilityPreviewVisibility));
        RefreshRuleSummary();
    }

    private async Task RefreshRuleDraftAsync(RuleDialogDraft draft)
    {
        var version = ++_previewRefreshVersion;
        await _data.ReloadImplicitWeightsAsync();
        if (!ReferenceEquals(RuleDraft, draft))
        {
            return;
        }

        var mode = (DrawMode)Math.Clamp(draft.SelectedDrawModeIndex, 0, 3);
        var filtered = GetFilteredMembers(draft.FilterOptions());
        var preview = _drawService.PreviewProbabilities(filtered, _data.State.Groups, new DrawOptions(mode));
        if (version != _previewRefreshVersion || !ReferenceEquals(RuleDraft, draft))
        {
            return;
        }

        UpdateProbabilityPreview(draft.ProbabilityPreview, preview);
        UpdateRuleSummary(
            draft.RuleSummaryRows,
            mode,
            NormalizeTargetCount(draft.TargetCount),
            filtered.Count,
            preview.Count,
            draft.FilterOptions());
    }

    private void RefreshRuleSummary()
    {
        var filtered = GetFilteredMembers(CurrentFilterOptions());
        UpdateRuleSummary(
            RuleSummaryRows,
            _settings.Current.DrawMode,
            NormalizeTargetCount(),
            filtered.Count,
            ProbabilityPreview.Count,
            CurrentFilterOptions());
    }

    private void UpdateRuleSummary(
        ObservableCollection<RuleSummaryRowViewModel> destination,
        DrawMode mode,
        int normalizedTargetCount,
        int filteredCount,
        int drawableCount,
        DrawFilterOptions filters)
    {
        destination.Clear();
        destination.Add(new RuleSummaryRowViewModel("当前模式", ModeText(mode), ModeDetail(mode)));
        destination.Add(new RuleSummaryRowViewModel("抽取人数", $"{normalizedTargetCount} 人", "每次按不放回方式抽取。"));
        destination.Add(new RuleSummaryRowViewModel("候选范围", $"筛选候选 {filteredCount} 人，可抽取 {drawableCount} 人", FilterSummary(filters)));
        destination.Add(new RuleSummaryRowViewModel("首轮概率", drawableCount > 0 ? "已生成" : "无可抽取候选", "多人抽取时，后续轮次会重新计算概率。"));
        destination.Add(new RuleSummaryRowViewModel("计数规则", CountRuleText(mode), CountRuleDetail(mode)));
        destination.Add(new RuleSummaryRowViewModel("小组加成", GroupBoostSummary(), "抽中启用联动的小组成员后，同组剩余成员会在后续轮次获得倍率。"));
    }

    private static void UpdateProbabilityPreview(
        ObservableCollection<DrawProbabilityPreviewRowViewModel> destination,
        IReadOnlyList<DrawProbabilityPreview> preview)
    {
        destination.Clear();
        foreach (var item in preview)
        {
            destination.Add(new DrawProbabilityPreviewRowViewModel(item));
        }
    }

    private IReadOnlyList<Member> GetFilteredMembers(DrawFilterOptions filters) =>
        DrawFilterService.Apply(_data.State.Members, _data.State.Groups, _data.State.DrawHistory, filters);

    private DrawFilterOptions CurrentFilterOptions() => new(
        GroupNameFromIndex(IncludeGroupFilterIndex),
        GroupNameFromIndex(ExcludeGroupFilterIndex),
        NormalizeRecentDrawCount(),
        OnlyNeverDrawn);

    private int NormalizeTargetCount() => NormalizeTargetCount(TargetCount);

    private static int NormalizeTargetCount(double value) =>
        double.IsFinite(value)
            ? Math.Clamp((int)Math.Round(value), 1, int.MaxValue)
            : 1;

    private int NormalizeRecentDrawCount() => NormalizeRecentDrawCount(ExcludeRecentDrawCount);

    private static int NormalizeRecentDrawCount(double value) =>
        double.IsFinite(value)
            ? Math.Clamp((int)Math.Round(value), 0, 10_000)
            : 0;

    private int NormalizeGroupIndex(int value) =>
        value > 0 && value < GroupFilterOptions.Count ? value : 0;

    private string? GroupNameFromIndex(int index) =>
        index > 0 && index < GroupFilterOptions.Count ? GroupFilterOptions[index] : null;

    private string FilterSummary(DrawFilterOptions filters)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.IncludeGroupName)) parts.Add($"只抽小组：{filters.IncludeGroupName}");
        if (!string.IsNullOrWhiteSpace(filters.ExcludeGroupName)) parts.Add($"排除小组：{filters.ExcludeGroupName}");
        if (filters.ExcludeRecentDrawCount > 0) parts.Add($"排除最近 {filters.ExcludeRecentDrawCount} 次记录");
        if (filters.OnlyNeverDrawn) parts.Add("只抽从未出现在历史中的成员");
        return parts.Count == 0 ? "未启用筛选。" : string.Join("；", parts);
    }

    private string GroupBoostSummary()
    {
        var enabled = _data.State.Groups.Count(group => group.IsDrawBoostEnabled && group.Members.Count > 0);
        return enabled == 0 ? "未配置启用联动的小组" : $"已启用 {enabled} 个小组";
    }

    private void RebuildGroupFilterOptions()
    {
        var include = GroupNameFromIndex(IncludeGroupFilterIndex);
        var exclude = GroupNameFromIndex(ExcludeGroupFilterIndex);
        _synchronizingGroups = true;
        GroupFilterOptions.Clear();
        GroupFilterOptions.Add("全部小组");
        foreach (var name in _data.State.Groups
            .Select(group => group.GroupName.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            GroupFilterOptions.Add(name);
        }

        IncludeGroupFilterIndex = IndexOfGroup(include);
        ExcludeGroupFilterIndex = IndexOfGroup(exclude);
        RuleDraft?.UpdateGroupOptions(GroupFilterOptions);
        _synchronizingGroups = false;
        NotifyFilterStateChanged();
    }

    private void RebuildPresets(string? selectedName = null)
    {
        selectedName ??= SelectedDrawPreset?.Name;
        DrawPresets.Clear();
        foreach (var preset in _data.State.DrawPresets
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            DrawPresets.Add(preset.Clone());
        }
        SelectedDrawPreset = DrawPresets.FirstOrDefault(preset =>
            string.Equals(preset.Name, selectedName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task HandleGroupsChangedAsync()
    {
        var groupNames = _data.State.Groups
            .Select(group => group.GroupName.Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settings = _settings.Current;
        var include = string.IsNullOrWhiteSpace(settings.DrawIncludeGroupName)
            || groupNames.Contains(settings.DrawIncludeGroupName.Trim())
            ? settings.DrawIncludeGroupName
            : null;
        var exclude = string.IsNullOrWhiteSpace(settings.DrawExcludeGroupName)
            || groupNames.Contains(settings.DrawExcludeGroupName.Trim())
            ? settings.DrawExcludeGroupName
            : null;
        if (!string.Equals(include, settings.DrawIncludeGroupName, StringComparison.Ordinal)
            || !string.Equals(exclude, settings.DrawExcludeGroupName, StringComparison.Ordinal))
        {
            await _settings.UpdateAsync(current =>
            {
                current.DrawIncludeGroupName = include;
                current.DrawExcludeGroupName = exclude;
            });
        }

        RebuildGroupFilterOptions();
        await RefreshProbabilityPreviewAsync();
    }

    private void LoadRulesFromSettings()
    {
        var settings = _settings.Current;
        _synchronizingGroups = true;
        SelectedDrawModeIndex = (int)settings.DrawMode;
        TargetCount = NormalizeTargetCount(settings.DrawTargetCount);
        IncludeGroupFilterIndex = IndexOfGroup(settings.DrawIncludeGroupName);
        ExcludeGroupFilterIndex = IndexOfGroup(settings.DrawExcludeGroupName);
        ExcludeRecentDrawCount = NormalizeRecentDrawCount(settings.DrawExcludeRecentDrawCount);
        OnlyNeverDrawn = settings.DrawOnlyNeverDrawn;
        _synchronizingGroups = false;
        NotifyFilterStateChanged();
    }

    private int IndexOfGroup(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? 0
            : Math.Max(0, GroupFilterOptions.IndexOf(name));

    private static string? GroupNameFromDraftIndex(int index, IList<string> options) =>
        index > 0 && index < options.Count ? options[index] : null;

    private void NotifyDrawStateChanged()
    {
        OnPropertyChanged(nameof(CanDiscardCurrentDraw));
        DiscardCurrentDrawCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFilterStateChanged()
    {
        OnPropertyChanged(nameof(CanClearDrawFilters));
        ClearDrawFiltersCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<DrawnMemberWeight> CreateWeightRows(IEnumerable<Member> members) =>
        members.Select(member => new DrawnMemberWeight(
            member.Name,
            member.ExplicitWeight,
            MemberRules.CalledWeight(member.CalledCount))).ToList();

    private static string CreateCompletionMessage(DrawResult result, DrawCommitOutcome outcome)
    {
        var message = result.Message ?? $"本次抽取完成，共 {result.SelectedMembers.Count} 人。";
        if (outcome.WasAutoReset) message += " 已自动重置权重。";
        if (!outcome.ClassIslandSucceeded) message += " 通知发送失败。";
        return message;
    }

    private static string ModeText(DrawMode mode) => mode switch
    {
        DrawMode.SmartRandom => "智能随机",
        DrawMode.FullRandom => "完全随机",
        DrawMode.Selection => "选择模式",
        _ => "加权模式"
    };

    private static string ModeDetail(DrawMode mode) => mode switch
    {
        DrawMode.SmartRandom => "按被点次数自动调整随机倾向。",
        DrawMode.FullRandom => "基础随机部分对每位候选人保持均等。",
        DrawMode.Selection => "生成候选后，点击姓名确认计入次数。",
        _ => "按成员权重与被点次数计算。"
    };

    private static string CountRuleText(DrawMode mode) => mode switch
    {
        DrawMode.FullRandom => "不计入被点次数",
        DrawMode.Selection => "确认后计入",
        _ => "抽取完成后自动计入"
    };

    private static string CountRuleDetail(DrawMode mode) => mode switch
    {
        DrawMode.FullRandom => "结果仍会写入抽取历史。",
        DrawMode.Selection => "未确认或丢弃的结果不会影响次数。",
        _ => "结果会写入抽取历史，并参与自动重置设置。"
    };
}

public partial class RuleDialogDraft : ObservableObject
{
    private readonly DrawPageViewModel _owner;
    private bool _initializing = true;

    public ObservableCollection<string> GroupFilterOptions { get; } = [];
    public ObservableCollection<DrawProbabilityPreviewRowViewModel> ProbabilityPreview { get; } = [];
    public ObservableCollection<RuleSummaryRowViewModel> RuleSummaryRows { get; } = [];

    [ObservableProperty] private int selectedDrawModeIndex;
    [ObservableProperty] private double targetCount;
    [ObservableProperty] private int includeGroupFilterIndex;
    [ObservableProperty] private int excludeGroupFilterIndex;
    [ObservableProperty] private double excludeRecentDrawCount;
    [ObservableProperty] private bool onlyNeverDrawn;

    public bool HasProbabilityPreview => ProbabilityPreview.Count > 0;
    public Visibility ProbabilityPreviewVisibility => HasProbabilityPreview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyProbabilityPreviewVisibility => HasProbabilityPreview ? Visibility.Collapsed : Visibility.Visible;

    internal RuleDialogDraft(
        DrawPageViewModel owner,
        int modeIndex,
        double count,
        int includeIndex,
        int excludeIndex,
        double recentCount,
        bool neverDrawn,
        IEnumerable<string> groupOptions)
    {
        _owner = owner;
        GroupFilterOptions = new ObservableCollection<string>(groupOptions);
        selectedDrawModeIndex = modeIndex;
        targetCount = count;
        includeGroupFilterIndex = includeIndex;
        excludeGroupFilterIndex = excludeIndex;
        excludeRecentDrawCount = recentCount;
        onlyNeverDrawn = neverDrawn;
        _initializing = false;
    }

    public DrawFilterOptions FilterOptions() => new(
        GroupNameFromIndex(IncludeGroupFilterIndex),
        GroupNameFromIndex(ExcludeGroupFilterIndex),
        NormalizeRecentCount(ExcludeRecentDrawCount),
        OnlyNeverDrawn);

    internal void UpdateGroupOptions(IEnumerable<string> groupOptions)
    {
        var include = GroupNameFromIndex(IncludeGroupFilterIndex);
        var exclude = GroupNameFromIndex(ExcludeGroupFilterIndex);
        GroupFilterOptions.Clear();
        foreach (var item in groupOptions) GroupFilterOptions.Add(item);
        IncludeGroupFilterIndex = IndexOf(include);
        ExcludeGroupFilterIndex = IndexOf(exclude);
        RequestRefresh();
    }

    partial void OnSelectedDrawModeIndexChanged(int value) => RequestRefresh();
    partial void OnTargetCountChanged(double value) => RequestRefresh();
    partial void OnIncludeGroupFilterIndexChanged(int value) => RequestRefresh();
    partial void OnExcludeGroupFilterIndexChanged(int value) => RequestRefresh();
    partial void OnExcludeRecentDrawCountChanged(double value) => RequestRefresh();
    partial void OnOnlyNeverDrawnChanged(bool value) => RequestRefresh();

    private void RequestRefresh()
    {
        if (!_initializing)
        {
            _owner.ScheduleRuleDraftRefresh(this);
        }
    }

    private string? GroupNameFromIndex(int index) =>
        index > 0 && index < GroupFilterOptions.Count ? GroupFilterOptions[index] : null;

    private int IndexOf(string? groupName) =>
        string.IsNullOrWhiteSpace(groupName) ? 0 : Math.Max(0, GroupFilterOptions.IndexOf(groupName));

    private static int NormalizeRecentCount(double value) =>
        double.IsFinite(value) ? Math.Clamp((int)Math.Round(value), 0, 10_000) : 0;
}

public sealed class DrawProbabilityPreviewRowViewModel
{
    public DrawProbabilityPreviewRowViewModel(DrawProbabilityPreview preview)
    {
        Name = preview.Name;
        ProbabilityText = $"{preview.Probability:P1}";
        FactorsText = $"权重 {preview.ExplicitWeight:G6} · 被点权重 {preview.CalledWeight:G4} · 已点 {preview.CalledCount} 次 · 小组倍率 {preview.GroupMultiplier:G4}";
    }

    public string Name { get; }
    public string ProbabilityText { get; }
    public string FactorsText { get; }
}

public sealed record RuleSummaryRowViewModel(string Label, string Value, string Detail);
