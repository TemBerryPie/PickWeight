using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class GroupRules
{
    public static GroupValidationResult ValidateGroups(IReadOnlyList<Group> groups, IReadOnlyList<Member> members)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var knownMembers = members
            .Select(member => member.Name.Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var groupName = group.GroupName.Trim();
            if (groupName.Length == 0)
            {
                errors.Add("小组名称不能为空。");
                continue;
            }

            if (!groupNames.Add(groupName))
            {
                errors.Add($"小组名称“{groupName}”重复。");
            }

            if (!double.IsFinite(group.SameDrawBoostMultiplier)
                || group.SameDrawBoostMultiplier < 1d
                || group.SameDrawBoostMultiplier > 10d)
            {
                errors.Add($"小组“{groupName}”的同轮概率倍率必须在 1 到 10 之间。");
            }

            if (group.Members.Count == 0)
            {
                warnings.Add($"小组“{groupName}”没有成员。");
            }

            var memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var memberName in group.Members)
            {
                var normalized = memberName.Trim();
                if (normalized.Length == 0)
                {
                    errors.Add($"小组“{groupName}”包含空成员名。");
                    continue;
                }

                if (!memberNames.Add(normalized))
                {
                    errors.Add($"小组“{groupName}”中成员“{normalized}”重复。");
                }

                if (!knownMembers.Contains(normalized))
                {
                    errors.Add($"小组“{groupName}”中的成员“{normalized}”不存在。");
                }
            }
        }

        return new GroupValidationResult(errors, warnings);
    }
}
