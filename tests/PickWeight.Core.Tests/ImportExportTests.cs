using PickWeight.Core.Models;
using PickWeight.Core.Services;
using System.Text.Json;

namespace PickWeight.Core.Tests;

public sealed class ImportExportTests
{
    [Fact]
    public void MemberImport_DeduplicatesWithoutOverwriting()
    {
        var members = new List<Member> { new() { Name = "Alice", ExplicitWeight = 250d } };

        var result = new MemberImportExportService().Import(" alice \nBob\nBob\n", members);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(250d, members[0].ExplicitWeight);
        Assert.Contains(members, member => member.Name == "Bob");
    }

    [Fact]
    public void MemberImport_ReadsExportedWeights()
    {
        var members = new List<Member>();
        const string content = "Alice 250.5\nBob 0.75";

        var result = new MemberImportExportService().Import(content, members);

        Assert.Equal(2, result.AddedCount);
        Assert.Contains(members, member => member.Name == "Alice" && member.ExplicitWeight == 250.5d);
        Assert.Contains(members, member => member.Name == "Bob" && member.ExplicitWeight == 0.75d);
    }

    [Fact]
    public void MemberImportPreview_DoesNotModifySourceAndClassifiesRows()
    {
        var members = new List<Member> { new() { Name = "Alice" } };
        const string content = "\nAlice\nBob 2\nBob\nCarol 1000001\nDan abc";

        var preview = new MemberImportExportService().PreviewImport(content, members);

        Assert.Single(members);
        Assert.Equal(1, preview.AddCount);
        Assert.Contains(preview.Items, item => item.Action == MemberImportAction.SkipBlank);
        Assert.Contains(preview.Items, item => item.Action == MemberImportAction.SkipDuplicateExisting);
        Assert.Contains(preview.Items, item => item.Action == MemberImportAction.SkipDuplicateInFile);
        Assert.Equal(2, preview.Items.Count(item => item.Action == MemberImportAction.ErrorInvalidWeight));
    }

    [Fact]
    public void MemberImportApply_AddsOnlyValidPreviewRows()
    {
        var service = new MemberImportExportService();
        var members = new List<Member> { new() { Name = "Alice" } };
        var preview = service.PreviewImport("Bob 2\nAlice\nCarol 1000001\nDan abc", members);

        var result = service.ApplyImport(preview, members);

        Assert.Equal(1, result.AddedCount);
        Assert.Contains(members, member => member.Name == "Bob" && member.ExplicitWeight == 2d);
        Assert.DoesNotContain(members, member => member.Name == "Carol");
        Assert.DoesNotContain(members, member => member.Name == "Dan");
    }

    [Fact]
    public void GroupImport_MergesAndSkipsUnknownMembers()
    {
        var members = new List<Member> { new() { Name = "Alice" }, new() { Name = "Bob" } };
        var groups = new List<Group> { new() { GroupName = "Team", Members = ["Alice"] } };
        const string json = """[{"groupName":"team","members":["Bob","Unknown"],"sameDrawBoostMultiplier":3.5}]""";

        var result = new GroupImportExportService().Import(json, groups, members);

        Assert.Equal(1, result.MergedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(["Alice", "Bob"], groups[0].Members);
        Assert.Equal(3.5d, groups[0].SameDrawBoostMultiplier);
    }

    [Fact]
    public void GroupRules_BlocksInvalidGroupsButAllowsEmptyGroup()
    {
        var members = new List<Member> { new() { Name = "Alice" } };
        var groups = new List<Group>
        {
            new() { GroupName = "Team", Members = ["Alice"] },
            new() { GroupName = "team", Members = [] },
            new() { GroupName = "Bad", Members = ["Unknown"], SameDrawBoostMultiplier = 20d }
        };

        var result = GroupRules.ValidateGroups(groups, members);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("重复", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("不存在", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("倍率", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("没有成员", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoryExport_FiltersByDateMemberAndCurrentGroup()
    {
        var service = new HistoryExportService();
        var history = new List<DrawHistoryEntry>
        {
            new() { DrawnAt = DateTimeOffset.Parse("2026-06-01T08:00:00+08:00"), MemberNames = ["Alice", "Bob"] },
            new() { DrawnAt = DateTimeOffset.Parse("2026-06-02T08:00:00+08:00"), MemberNames = ["Carol"] }
        };
        var groups = new List<Group> { new() { GroupName = "A", Members = ["Bob"] } };

        var result = service.Filter(
            history,
            groups,
            new HistoryFilterOptions(
                DateTimeOffset.Parse("2026-06-01T00:00:00+08:00"),
                DateTimeOffset.Parse("2026-06-01T00:00:00+08:00"),
                "Bob",
                "A"));

        Assert.Single(result);
        Assert.Equal(["Alice", "Bob"], result[0].MemberNames);
    }

    [Fact]
    public void HistoryExport_EscapesCsvAndIncludesJsonGroups()
    {
        var service = new HistoryExportService();
        var history = new List<DrawHistoryEntry>
        {
            new() { DrawnAt = DateTimeOffset.Parse("2026-06-01T08:00:00+08:00"), MemberNames = ["A,\"B\""] }
        };
        var groups = new List<Group> { new() { GroupName = "一组", Members = ["A,\"B\""] } };

        var csv = service.ExportCsv(history, groups);
        var json = service.ExportJson(history, groups, new HistoryFilterOptions(GroupName: "一组"), DateTimeOffset.Parse("2026-06-02T00:00:00+08:00"));

        Assert.Contains("\"A,\"\"B\"\"\"", csv);
        Assert.Contains("一组", csv);
        Assert.Contains("\"groupMappingSource\": \"currentGroups\"", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("一组", document.RootElement
            .GetProperty("entries")[0]
            .GetProperty("members")[0]
            .GetProperty("groups")[0]
            .GetString());
    }
}
