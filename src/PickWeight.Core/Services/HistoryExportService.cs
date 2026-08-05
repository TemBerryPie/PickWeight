using System.Globalization;
using System.Text;
using System.Text.Json;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public sealed class HistoryExportService : IHistoryExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<DrawHistoryEntry> Filter(
        IReadOnlyList<DrawHistoryEntry> history,
        IReadOnlyList<Group> groups,
        HistoryFilterOptions options)
    {
        var groupMembers = GetGroupMembers(groups, options.GroupName);
        var memberName = Normalize(options.MemberName ?? string.Empty);
        var start = options.StartDate?.LocalDateTime.Date;
        var endExclusive = options.EndDate?.LocalDateTime.Date.AddDays(1);

        return history
            .Where(entry =>
            {
                var localDate = entry.DrawnAt.LocalDateTime;
                if (start is not null && localDate < start.Value)
                {
                    return false;
                }

                if (endExclusive is not null && localDate >= endExclusive.Value)
                {
                    return false;
                }

                if (memberName.Length > 0 && !entry.MemberNames.Any(name => string.Equals(Normalize(name), memberName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (groupMembers is not null && !entry.MemberNames.Any(name => groupMembers.Contains(Normalize(name))))
                {
                    return false;
                }

                return true;
            })
            .OrderByDescending(entry => entry.DrawnAt)
            .Select(entry => entry.Clone())
            .ToList();
    }

    public string ExportCsv(IReadOnlyList<DrawHistoryEntry> history, IReadOnlyList<Group> groups)
    {
        var memberGroups = BuildMemberGroups(groups);
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendLine("drawnAt,date,time,memberName,groupNames");
        foreach (var entry in history.OrderByDescending(entry => entry.DrawnAt))
        {
            foreach (var memberName in entry.MemberNames)
            {
                var local = entry.DrawnAt.LocalDateTime;
                var groupNames = memberGroups.TryGetValue(Normalize(memberName), out var names)
                    ? string.Join("; ", names.Order(StringComparer.OrdinalIgnoreCase))
                    : string.Empty;
                builder.AppendLine(string.Join(
                    ',',
                    EscapeCsv(entry.DrawnAt.ToString("O", CultureInfo.InvariantCulture)),
                    EscapeCsv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    EscapeCsv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    EscapeCsv(memberName),
                    EscapeCsv(groupNames)));
            }
        }

        return builder.ToString();
    }

    public string ExportJson(
        IReadOnlyList<DrawHistoryEntry> history,
        IReadOnlyList<Group> groups,
        HistoryFilterOptions options,
        DateTimeOffset exportedAt)
    {
        var memberGroups = BuildMemberGroups(groups);
        var payload = new
        {
            version = 1,
            exportedAt,
            groupMappingSource = "currentGroups",
            filters = new
            {
                startDate = options.StartDate?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = options.EndDate?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                memberName = Normalize(options.MemberName ?? string.Empty).Length == 0 ? null : options.MemberName,
                groupName = Normalize(options.GroupName ?? string.Empty).Length == 0 ? null : options.GroupName
            },
            entries = history.OrderByDescending(entry => entry.DrawnAt).Select(entry => new
            {
                drawnAt = entry.DrawnAt,
                members = entry.MemberNames.Select(name => new
                {
                    name,
                    groups = memberGroups.TryGetValue(Normalize(name), out var names)
                        ? names.Order(StringComparer.OrdinalIgnoreCase).ToArray()
                        : Array.Empty<string>()
                })
            })
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
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
        return group?.Members
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<string>> BuildMemberGroups(IEnumerable<Group> groups)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var groupName = Normalize(group.GroupName);
            if (groupName.Length == 0)
            {
                continue;
            }

            foreach (var memberName in group.Members.Select(Normalize).Where(name => name.Length > 0))
            {
                if (!result.TryGetValue(memberName, out var groupNames))
                {
                    groupNames = [];
                    result[memberName] = groupNames;
                }

                if (!groupNames.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                {
                    groupNames.Add(groupName);
                }
            }
        }

        return result;
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string Normalize(string value) => value.Trim();
}
