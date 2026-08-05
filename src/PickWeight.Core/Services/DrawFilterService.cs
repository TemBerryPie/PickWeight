using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class DrawFilterService
{
    public static IReadOnlyList<Member> Apply(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        IReadOnlyList<DrawHistoryEntry> history,
        DrawFilterOptions options)
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

        return query.ToList();
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
