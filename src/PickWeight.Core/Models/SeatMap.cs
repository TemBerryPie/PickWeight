namespace PickWeight.Core.Models;

/// <summary>
/// 列式座位表。教室按纵列排布，每列独立设置人数，列与列之间可并排或留过道。
/// 方位用靠门/靠窗描述，避免视角歧义。
/// </summary>
public sealed class SeatMap
{
    /// <summary>从靠门侧到靠窗侧依次排列的列。</summary>
    public List<SeatColumn> Columns { get; set; } = [];

    /// <summary>靠门侧在渲染时位于哪一端，仅影响显示方向。</summary>
    public SeatSideOrientation DoorSide { get; set; } = SeatSideOrientation.Start;

    public SeatMap Clone() => new()
    {
        Columns = Columns.Select(col => col.Clone()).ToList(),
        DoorSide = DoorSide
    };
}

/// <summary>
/// 座位表的一列。每列人数独立，支持不规则教室。
/// </summary>
public sealed class SeatColumn
{
    /// <summary>该列座位数（行数）。</summary>
    public int SeatCount { get; set; } = 6;

    /// <summary>此列之后是否留过道空格。</summary>
    public bool HasAisleAfter { get; set; }

    /// <summary>
    /// 成员姓名列表，下标对应第几排（从前往后 0-based）。
    /// null 表示空位。长度始终与 <see cref="SeatCount"/> 对齐。
    /// </summary>
    public List<string?> MemberNames { get; set; } = [];

    public SeatColumn Clone() => new()
    {
        SeatCount = SeatCount,
        HasAisleAfter = HasAisleAfter,
        MemberNames = [.. MemberNames]
    };
}

/// <summary>
/// 座位表方位：靠门侧在显示区域的哪一端。
/// </summary>
public enum SeatSideOrientation
{
    /// <summary>靠门侧在左端（或上端）。</summary>
    Start = 0,

    /// <summary>靠门侧在右端（或下端）。</summary>
    End = 1
}
