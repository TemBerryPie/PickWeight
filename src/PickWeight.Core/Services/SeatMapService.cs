using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 座位表核心操作：校验、增删列、改人数、分配/移除成员、自动排入、区域统计。
/// 不依赖 UI，可单元测试。
/// </summary>
public static class SeatMapService
{
    public const int MaxColumns = 20;
    public const int MaxSeatCountPerColumn = 40;
    public const int MaxTotalSeats = 200;

    /// <summary>校验座位表结构，返回错误信息；null 表示有效。</summary>
    public static string? Validate(SeatMap? map)
    {
        if (map is null)
        {
            return null;
        }

        if (map.Columns.Count > MaxColumns)
        {
            return $"列数不能超过 {MaxColumns}。";
        }

        var total = 0;
        foreach (var column in map.Columns)
        {
            if (column.SeatCount < 0 || column.SeatCount > MaxSeatCountPerColumn)
            {
                return $"每列人数必须在 0 到 {MaxSeatCountPerColumn} 之间。";
            }

            if (column.MemberNames.Count > column.SeatCount)
            {
                return "座位表数据损坏：成员数量超过座位数。";
            }

            total += column.SeatCount;
        }

        if (total > MaxTotalSeats)
        {
            return $"总座位数不能超过 {MaxTotalSeats}。";
        }

        // 姓名唯一性
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in map.Columns)
        {
            foreach (var name in column.MemberNames)
            {
                if (name is null)
                {
                    continue;
                }

                if (!seen.Add(name.Trim()))
                {
                    return $"成员“{name}”出现在多个座位，请先修正。";
                }
            }
        }

        return null;
    }

    /// <summary>在末尾追加一列，默认人数取当前最大列。</summary>
    public static SeatColumn AddColumn(SeatMap map, int? seatCount = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var count = seatCount ?? (map.Columns.Count == 0
            ? 6
            : map.Columns.Max(column => column.SeatCount));
        count = Math.Clamp(count, 1, MaxSeatCountPerColumn);
        var column = new SeatColumn { SeatCount = count, MemberNames = new List<string?>(new string?[count]) };
        map.Columns.Add(column);
        return column;
    }

    /// <summary>调整某列人数。超出部分（有人）先截断到空位为止，返回被移出的姓名。</summary>
    public static IReadOnlyList<string> ResizeColumn(SeatMap map, int columnIndex, int newCount)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (columnIndex < 0 || columnIndex >= map.Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        newCount = Math.Clamp(newCount, 0, MaxSeatCountPerColumn);
        var column = map.Columns[columnIndex];
        var removed = new List<string>();
        while (column.MemberNames.Count > newCount)
        {
            var last = column.MemberNames[^1];
            if (last is not null)
            {
                removed.Add(last);
            }

            column.MemberNames.RemoveAt(column.MemberNames.Count - 1);
        }

        while (column.MemberNames.Count < newCount)
        {
            column.MemberNames.Add(null);
        }

        column.SeatCount = newCount;
        return removed;
    }

    /// <summary>删除整列，返回被移出的成员姓名。</summary>
    public static IReadOnlyList<string> RemoveColumn(SeatMap map, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (columnIndex < 0 || columnIndex >= map.Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        var removed = map.Columns[columnIndex].MemberNames
            .Where(name => name is not null)
            .Cast<string>()
            .ToList();
        map.Columns.RemoveAt(columnIndex);
        return removed;
    }

    /// <summary>把成员放入指定座位，若该座位已有人则返回被换出的姓名。</summary>
    public static string? Assign(SeatMap map, int columnIndex, int rowIndex, string memberName)
    {
        ArgumentNullException.ThrowIfNull(map);
        var column = GetColumn(map, columnIndex, rowIndex);
        var existing = column.MemberNames[rowIndex];
        column.MemberNames[rowIndex] = memberName.Trim();
        return existing;
    }

    /// <summary>清空指定座位，返回原成员姓名。</summary>
    public static string? Clear(SeatMap map, int columnIndex, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(map);
        var column = GetColumn(map, columnIndex, rowIndex);
        var existing = column.MemberNames[rowIndex];
        column.MemberNames[rowIndex] = null;
        return existing;
    }

    /// <summary>某成员当前所在位置，未排座返回 null。</summary>
    public static (int Column, int Row)? FindSeat(SeatMap map, string memberName)
    {
        ArgumentNullException.ThrowIfNull(map);
        var normalized = memberName.Trim();
        for (var columnIndex = 0; columnIndex < map.Columns.Count; columnIndex++)
        {
            var names = map.Columns[columnIndex].MemberNames;
            for (var rowIndex = 0; rowIndex < names.Count; rowIndex++)
            {
                if (string.Equals(names[rowIndex]?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return (columnIndex, rowIndex);
                }
            }
        }

        return null;
    }

    /// <summary>把成员从座位上移除（若已排座）。</summary>
    public static void RemoveMember(SeatMap map, string memberName)
    {
        ArgumentNullException.ThrowIfNull(map);
        var seat = FindSeat(map, memberName);
        if (seat is { } position)
        {
            Clear(map, position.Column, position.Row);
        }
    }

    /// <summary>自动排入：用给定顺序的名单依次填充空位，返回未能排入的姓名。</summary>
    public static IReadOnlyList<string> AutoAssign(
        SeatMap map,
        IEnumerable<string> memberNames)
    {
        ArgumentNullException.ThrowIfNull(map);
        var unassigned = new List<string>();
        var seatQueue = map.Columns
            .SelectMany((column, columnIndex) => column.MemberNames
                .Select((name, rowIndex) => (Column: columnIndex, Row: rowIndex, Name: name)))
            .Where(seat => seat.Name is null)
            .Select(seat => (seat.Column, seat.Row))
            .ToList();

        var queueIndex = 0;
        foreach (var name in memberNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var normalized = name.Trim();
            if (FindSeat(map, normalized) is not null)
            {
                continue; // 已排座
            }

            if (queueIndex < seatQueue.Count)
            {
                var (columnIndex, rowIndex) = seatQueue[queueIndex++];
                map.Columns[columnIndex].MemberNames[rowIndex] = normalized;
            }
            else
            {
                unassigned.Add(normalized);
            }
        }

        return unassigned;
    }

    /// <summary>已排座成员集合。</summary>
    public static IReadOnlySet<string> SeatedNames(SeatMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in map.Columns)
        {
            foreach (var name in column.MemberNames)
            {
                if (name is not null)
                {
                    result.Add(name.Trim());
                }
            }
        }

        return result;
    }

    /// <summary>按区域过滤座位。rowIndex 按每列自身下标计算，人数不足的列跳过。</summary>
    public static IReadOnlyList<string> MembersInRegion(
        SeatMap map,
        SeatRegion region,
        int columnIndex = -1,
        int rowIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = new List<string>();
        var columns = map.Columns;
        // logicalIndex 始终表示「从靠门侧数的列号」，与渲染方向无关
        var reversed = map.DoorSide == SeatSideOrientation.End;

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var logicalIndex = reversed ? columns.Count - 1 - index : index;
            // 靠门半区 = 逻辑列号靠前的一半；靠窗半区 = 其余
            var doorHalfBoundary = (columns.Count - 1) / 2;
            var inHalf = region switch
            {
                SeatRegion.DoorHalf => logicalIndex <= doorHalfBoundary,
                SeatRegion.WindowHalf => logicalIndex > doorHalfBoundary,
                _ => true
            };
            if (!inHalf)
            {
                continue;
            }

            for (var row = 0; row < column.MemberNames.Count; row++)
            {
                var name = column.MemberNames[row];
                if (name is null)
                {
                    continue;
                }

                if (region == SeatRegion.SpecificRow && row != rowIndex)
                {
                    continue;
                }

                if (region == SeatRegion.SpecificColumn && logicalIndex != columnIndex)
                {
                    continue;
                }

                result.Add(name.Trim());
            }
        }

        return result;
    }

    /// <summary>
    /// 计算视觉渲染顺序与过道归属，供页面按序渲染每列并追加过道。
    /// 视觉下标 0 始终在最靠门的一端：DoorSide=Start 时按逻辑列 0..N-1，DoorSide=End 时按 N-1..0。
    /// 过道归属：非镜像时 HasAisleAfterVisual 等于该列 HasAisleAfter；
    /// 镜像时过道归「上一逻辑列」，即 logicalIndex&gt;0 且 columns[logicalIndex-1].HasAisleAfter，
    /// 使镜像后过道仍落在两列之间而不是出现在门侧边缘。
    /// </summary>
    public static IReadOnlyList<(int LogicalIndex, bool HasAisleAfterVisual)> ComputeVisualLayout(SeatMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var reversed = map.DoorSide == SeatSideOrientation.End;
        var result = new List<(int LogicalIndex, bool HasAisleAfterVisual)>(map.Columns.Count);
        for (var index = 0; index < map.Columns.Count; index++)
        {
            var logicalIndex = reversed ? map.Columns.Count - 1 - index : index;
            var hasAisleAfter = reversed
                ? logicalIndex > 0 && map.Columns[logicalIndex - 1].HasAisleAfter
                : map.Columns[logicalIndex].HasAisleAfter;
            result.Add((logicalIndex, hasAisleAfter));
        }

        return result;
    }

    private static SeatColumn GetColumn(SeatMap map, int columnIndex, int rowIndex)
    {
        if (columnIndex < 0 || columnIndex >= map.Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        var column = map.Columns[columnIndex];
        if (rowIndex < 0 || rowIndex >= column.MemberNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        return column;
    }
}
