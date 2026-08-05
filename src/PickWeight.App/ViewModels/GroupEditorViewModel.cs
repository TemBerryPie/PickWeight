using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PickWeight.Core.Models;

namespace PickWeight_App.ViewModels;

public partial class GroupEditorViewModel : ObservableObject
{
    private readonly Action _changed;
    public ObservableCollection<string> Members { get; } = [];

    [ObservableProperty]
    private string groupName = string.Empty;
    [ObservableProperty]
    private bool isDrawBoostEnabled = true;
    [ObservableProperty]
    private double sameDrawBoostMultiplier = 1.5d;

    public GroupEditorViewModel(Group group, Action changed)
    {
        _changed = changed;
        GroupName = group.GroupName;
        IsDrawBoostEnabled = group.IsDrawBoostEnabled;
        SameDrawBoostMultiplier = group.SameDrawBoostMultiplier;
        foreach (var member in group.Members) Members.Add(member);
    }

    partial void OnGroupNameChanged(string value) => _changed();
    partial void OnIsDrawBoostEnabledChanged(bool value) => _changed();
    partial void OnSameDrawBoostMultiplierChanged(double value) => _changed();
    public Group ToModel(bool clampMultiplier = true) => new()
    {
        GroupName = GroupName.Trim(),
        Members = [.. Members],
        IsDrawBoostEnabled = IsDrawBoostEnabled,
        SameDrawBoostMultiplier = clampMultiplier
            ? Math.Clamp(double.IsFinite(SameDrawBoostMultiplier) ? SameDrawBoostMultiplier : 1d, 1d, 10d)
            : SameDrawBoostMultiplier
    };
}
