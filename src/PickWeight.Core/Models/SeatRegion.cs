namespace PickWeight.Core.Models;

/// <summary>座位抽取区域。</summary>
public enum SeatRegion
{
    /// <summary>全部座位。</summary>
    All = 0,

    /// <summary>指定列（逻辑列号，靠门侧为 0）。</summary>
    SpecificColumn = 1,

    /// <summary>指定排（按每列自身下标，人数不足的列跳过）。</summary>
    SpecificRow = 2,

    /// <summary>靠门半区。</summary>
    DoorHalf = 3,

    /// <summary>靠窗半区。</summary>
    WindowHalf = 4
}
