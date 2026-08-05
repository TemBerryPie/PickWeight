using System.Text.Json;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public sealed class GroupImportExportService : IGroupImportExportService
{
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public ImportResult Import(string content, IList<Group> groups, IReadOnlyList<Member> members)
    {
        var imported = JsonSerializer.Deserialize<List<Group>>(content, _options)
            ?? throw new InvalidDataException("小组 JSON 格式无效。");
        var knownMembers = members.ToDictionary(member => member.Name.Trim(), member => member.Name, StringComparer.OrdinalIgnoreCase);
        var existingGroups = groups.ToDictionary(group => group.GroupName.Trim(), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var merged = 0;
        var skipped = 0;

        foreach (var importedGroup in imported)
        {
            var groupName = importedGroup.GroupName.Trim();
            if (groupName.Length == 0)
            {
                skipped++;
                continue;
            }

            if (!existingGroups.TryGetValue(groupName, out var target))
            {
                target = new Group
                {
                    GroupName = groupName,
                    IsDrawBoostEnabled = importedGroup.IsDrawBoostEnabled,
                    SameDrawBoostMultiplier = Math.Clamp(
                        double.IsFinite(importedGroup.SameDrawBoostMultiplier) ? importedGroup.SameDrawBoostMultiplier : 1d,
                        1d,
                        10d)
                };
                groups.Add(target);
                existingGroups[groupName] = target;
                added++;
            }
            else
            {
                target.IsDrawBoostEnabled = importedGroup.IsDrawBoostEnabled;
                target.SameDrawBoostMultiplier = Math.Clamp(
                    double.IsFinite(importedGroup.SameDrawBoostMultiplier) ? importedGroup.SameDrawBoostMultiplier : 1d,
                    1d,
                    10d);
                merged++;
            }

            var targetNames = new HashSet<string>(target.Members, StringComparer.OrdinalIgnoreCase);
            foreach (var importedName in importedGroup.Members)
            {
                var normalized = importedName.Trim();
                if (!knownMembers.TryGetValue(normalized, out var displayName))
                {
                    skipped++;
                    continue;
                }

                if (targetNames.Add(displayName))
                {
                    target.Members.Add(displayName);
                }
            }
        }

        return new ImportResult(added, merged, skipped);
    }

    public string Export(IEnumerable<Group> groups) => JsonSerializer.Serialize(groups, _options);
}
