namespace PickWeight.Core.Models;

public sealed class DrawHistoryEntry
{
    public DateTimeOffset DrawnAt { get; set; }

    /// <summary>抽中的成员姓名。保留此字段以兼容旧数据与既有导出格式。</summary>
    public List<string> MemberNames { get; set; } = [];

    /// <summary>逐人明细，含课堂表现评价。旧数据加载时由 <see cref="MemberNames"/> 补齐。</summary>
    public List<DrawRecord> Records { get; set; } = [];

    /// <summary>科目，来自 ClassIsland 课时上下文。</summary>
    public string? Subject { get; set; }

    /// <summary>第几节课，来自 ClassIsland 课时上下文。</summary>
    public int? PeriodIndex { get; set; }

    /// <summary>产生该记录时的轮次序号。</summary>
    public int? RoundNumber { get; set; }

    public DrawHistoryEntry Clone() => new()
    {
        DrawnAt = DrawnAt,
        MemberNames = [.. MemberNames],
        Records = Records.Select(record => record.Clone()).ToList(),
        Subject = Subject,
        PeriodIndex = PeriodIndex,
        RoundNumber = RoundNumber
    };

    /// <summary>
    /// 用 <see cref="MemberNames"/> 补齐缺失的明细记录，使旧数据也能参与评价统计。
    /// </summary>
    public void EnsureRecords()
    {
        if (Records.Count > 0 || MemberNames.Count == 0)
        {
            return;
        }

        Records = MemberNames
            .Select(name => new DrawRecord { Name = name })
            .ToList();
    }
}
