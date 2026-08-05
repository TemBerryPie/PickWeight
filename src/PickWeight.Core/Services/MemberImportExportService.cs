using System.Globalization;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public sealed class MemberImportExportService : IMemberImportExportService
{
    public ImportResult Import(string content, IList<Member> members)
    {
        var preview = PreviewImport(content, members);
        return ApplyImport(preview, members);
    }

    public MemberImportPreview PreviewImport(string content, IEnumerable<Member> members)
    {
        var existing = new HashSet<string>(members.Select(member => member.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<MemberImportPreviewItem>();
        var lineNumber = 0;

        foreach (var line in content.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNumber++;
            var trimmedLine = line.Trim();
            var parse = TryParseLine(trimmedLine);
            if (parse.Name.Length == 0)
            {
                items.Add(new MemberImportPreviewItem(lineNumber, line, string.Empty, 100d, MemberImportAction.SkipBlank, "空行"));
                continue;
            }

            if (parse.Error is not null)
            {
                items.Add(new MemberImportPreviewItem(lineNumber, line, parse.Name, parse.ExplicitWeight, MemberImportAction.ErrorInvalidWeight, parse.Error));
                continue;
            }

            if (existing.Contains(parse.Name))
            {
                items.Add(new MemberImportPreviewItem(lineNumber, line, parse.Name, parse.ExplicitWeight, MemberImportAction.SkipDuplicateExisting, "成员已存在"));
                continue;
            }

            if (!fileNames.Add(parse.Name))
            {
                items.Add(new MemberImportPreviewItem(lineNumber, line, parse.Name, parse.ExplicitWeight, MemberImportAction.SkipDuplicateInFile, "导入文件内重复"));
                continue;
            }

            items.Add(new MemberImportPreviewItem(lineNumber, line, parse.Name, parse.ExplicitWeight, MemberImportAction.Add, "新增"));
        }

        return new MemberImportPreview(items);
    }

    public ImportResult ApplyImport(MemberImportPreview preview, IList<Member> members)
    {
        var existing = new HashSet<string>(members.Select(member => member.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = 0;
        foreach (var item in preview.Items)
        {
            if (item.Action != MemberImportAction.Add || item.Name.Length == 0 || !existing.Add(item.Name))
            {
                skipped++;
                continue;
            }

            members.Add(new Member { Name = item.Name, ExplicitWeight = item.ExplicitWeight });
            added++;
        }

        return new ImportResult(added, 0, skipped);
    }

    public string Export(IEnumerable<Member> members) =>
        string.Join(Environment.NewLine, members.Select(member =>
            $"{member.Name} {member.ExplicitWeight.ToString("0.########", CultureInfo.InvariantCulture)}"));

    private static (string Name, double ExplicitWeight, string? Error) TryParseLine(string line)
    {
        if (line.Length == 0)
        {
            return (string.Empty, 100d, null);
        }

        var separatorIndex = line.LastIndexOf(' ');
        if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
        {
            return (line, 100d, null);
        }

        var weightText = line[(separatorIndex + 1)..].Trim();
        if (!double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var explicitWeight))
        {
            return (line[..separatorIndex].TrimEnd(), 100d, "权重必须是有效数字");
        }

        var name = line[..separatorIndex].TrimEnd();
        try
        {
            MemberRules.ValidateWeight(explicitWeight, nameof(Member.ExplicitWeight));
        }
        catch (InvalidDataException)
        {
            return (name, explicitWeight, "权重必须是 0 到 1000000 之间的有限数值");
        }

        return (name, explicitWeight, null);
    }
}
