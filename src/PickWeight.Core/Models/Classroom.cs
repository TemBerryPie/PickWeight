namespace PickWeight.Core.Models;

/// <summary>
/// 一个班级的全部数据。每个班级对应磁盘上的一个文件夹，文件夹名即 <see cref="Name"/>，
/// 班级数据与该班的隐式权重配置存放在一起，互不干扰。
/// </summary>
public sealed class Classroom
{
    /// <summary>班级名称，同时是数据文件夹名，在所有班级中唯一。</summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>科目，仅用于历史记录标注，不参与班级切换判定。</summary>
    public string? Subject { get; set; }

    public List<Member> Members { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<DrawHistoryEntry> DrawHistory { get; set; } = [];
    public List<DrawPreset> DrawPresets { get; set; } = [];
    public SeatMap? SeatMap { get; set; }
    public RoundState Round { get; set; } = new();

    public const string DefaultName = "默认班级";

    public Classroom Clone() => new()
    {
        Name = Name,
        Subject = Subject,
        Members = Members.Select(member => member.Clone()).ToList(),
        Groups = Groups.Select(group => group.Clone()).ToList(),
        DrawHistory = DrawHistory.Select(entry => entry.Clone()).ToList(),
        DrawPresets = DrawPresets.Select(preset => preset.Clone()).ToList(),
        SeatMap = SeatMap?.Clone(),
        Round = Round.Clone()
    };
}
