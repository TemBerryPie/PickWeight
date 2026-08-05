namespace PickWeight.Core.Models;

/// <summary>
/// 小组仅通过姓名引用成员，允许同一成员同时属于多个小组。
/// </summary>
public sealed class Group
{
    public string GroupName { get; set; } = string.Empty;
    public List<string> Members { get; set; } = [];
    public bool IsDrawBoostEnabled { get; set; } = true;
    public double SameDrawBoostMultiplier { get; set; } = 1.5d;

    public Group Clone() => new()
    {
        GroupName = GroupName,
        Members = [.. Members],
        IsDrawBoostEnabled = IsDrawBoostEnabled,
        SameDrawBoostMultiplier = SameDrawBoostMultiplier
    };
}
