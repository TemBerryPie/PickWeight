using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class DrawFilterService
{
    public static IReadOnlyList<Member> Apply(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        IReadOnlyList<DrawHistoryEntry> history,
        DrawFilterOptions options,
        SeatMap? seatMap = null)
    {
        IEnumerable<Member> query = members;
        var includeNames = GetGroupMembers(groups, options.IncludeGroupName);
        if (includeNames is not null)
        {
            query = query.Where(member => includeNames.Contains(Normalize(member.Name)));
        }

        var excludeNames = GetGroupMembers(groups, options.ExcludeGroupName);
        if (excludeNames is not null)
        {
            query = query.Where(member => !excludeNames.Contains(Normalize(member.Name)));
        }

        if (options.ExcludeRecentDrawCount > 0)
        {
            var recentNames = history
                .OrderByDescending(entry => entry.DrawnAt)
                .Take(options.ExcludeRecentDrawCount)
                .SelectMany(entry => entry.MemberNames)
                .Select(Normalize)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            query = query.Where(member => !recentNames.Contains(Normalize(member.Name)));
        }

        if (options.OnlyNeverDrawn)
        {
            var drawnNames = history
                .SelectMany(entry => entry.MemberNames)
                .Select(Normalize)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            query = query.Where(member => !drawnNames.Contains(Normalize(member.Name)));
        }

        // 一轮不重复：排除本轮已抽到的成员
        if (options.ExcludeRoundDrawnNames is { Count: > 0 })
        {
            query = query.Where(member => !options.ExcludeRoundDrawnNames.Contains(Normalize(member.Name)));
        }

        // 今日请假：默认不参与抽取
        if (options.ExcludeAbsentNames is { Count: > 0 })
        {
            query = query.Where(member => !options.ExcludeAbsentNames.Contains(Normalize(member.Name)));
        }

        // 座位区域过滤
        if (seatMap is not null
            && options.SeatFilter is { Region: not SeatRegion.All } seatFilter)
        {
            var seatNames = SeatMapService.MembersInRegion(
                seatMap,
                seatFilter.Region,
                seatFilter.ColumnIndex,
                seatFilter.RowIndex)
                .Select(Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            query = query.Where(member => seatNames.Contains(Normalize(member.Name)));
        }

        return query.ToList();
    }


    /// <summary>
    /// 计算轮转统计：本轮已抽人数、可抽人数、是否已抽空需要翻轮。
    /// </summary>
    public static RoundProgress ComputeRoundProgress(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        IReadOnlyList<DrawHistoryEntry> history,
        DrawFilterOptions baseOptions,
        IReadOnlySet<string> roundDrawnNames,
        IReadOnlySet<string> absentNames)
    {
        var baseFiltered = Apply(members, groups, history, baseOptions).Select(member => Normalize(member.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drawn = baseFiltered.Intersect(roundDrawnNames).Count();
        var drawable = Apply(
            members,
            groups,
            history,
            baseOptions with
            {
                ExcludeRoundDrawnNames = roundDrawnNames,
                ExcludeAbsentNames = absentNames
            }).Count;
        return new RoundProgress(drawn, drawable);
    }

    public static IReadOnlySet<string> GetAbsentNames(IEnumerable<Member> members)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return members
            .Where(member => member.AbsentOn == today)
            .Select(member => Normalize(member.Name))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string>? GetGroupMembers(IEnumerable<Group> groups, string? groupName)
    {
        var normalizedGroupName = Normalize(groupName ?? string.Empty);
        if (normalizedGroupName.Length == 0)
        {
            return null;
        }

        var group = groups.FirstOrDefault(group =>
            string.Equals(Normalize(group.GroupName), normalizedGroupName, StringComparison.OrdinalIgnoreCase));
        return group is null
            ? []
            : group.Members
                .Select(Normalize)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Trim();
}
