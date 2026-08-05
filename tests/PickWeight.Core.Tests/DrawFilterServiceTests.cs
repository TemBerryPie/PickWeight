using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class DrawFilterServiceTests
{
    [Fact]
    public void Apply_IncludesAndExcludesGroups()
    {
        var members = CreateMembers("A", "B", "C");
        var groups = new[]
        {
            new Group { GroupName = "Include", Members = ["A", "B"] },
            new Group { GroupName = "Exclude", Members = ["B"] }
        };

        var result = DrawFilterService.Apply(
            members,
            groups,
            [],
            new DrawFilterOptions(IncludeGroupName: "Include", ExcludeGroupName: "Exclude"));

        Assert.Equal(["A"], result.Select(member => member.Name));
    }

    [Fact]
    public void Apply_ExcludesRecentDraws()
    {
        var history = new[]
        {
            new DrawHistoryEntry { DrawnAt = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00"), MemberNames = ["A"] },
            new DrawHistoryEntry { DrawnAt = DateTimeOffset.Parse("2026-06-14T11:00:00+08:00"), MemberNames = ["B"] }
        };

        var result = DrawFilterService.Apply(
            CreateMembers("A", "B", "C"),
            [],
            history,
            new DrawFilterOptions(ExcludeRecentDrawCount: 1));

        Assert.Equal(["A", "C"], result.Select(member => member.Name));
    }

    [Fact]
    public void Apply_OnlyNeverDrawnMembers()
    {
        var history = new[]
        {
            new DrawHistoryEntry { DrawnAt = DateTimeOffset.Now, MemberNames = ["A", "B"] }
        };

        var result = DrawFilterService.Apply(
            CreateMembers("A", "B", "C"),
            [],
            history,
            new DrawFilterOptions(OnlyNeverDrawn: true));

        Assert.Equal(["C"], result.Select(member => member.Name));
    }

    [Fact]
    public void Apply_ReturnsEmptyWhenFiltersRemoveEveryone()
    {
        var result = DrawFilterService.Apply(
            CreateMembers("A"),
            [new Group { GroupName = "Only", Members = ["A"] }],
            [],
            new DrawFilterOptions(ExcludeGroupName: "Only"));

        Assert.Empty(result);
    }

    private static List<Member> CreateMembers(params string[] names) =>
        names.Select(name => new Member { Name = name }).ToList();
}
