using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class RoundFilterTests
{
    private static List<Member> CreateMembers(params string[] names) =>
        names.Select(name => new Member { Name = name }).ToList();

    [Fact]
    public void Apply_ExcludesRoundDrawnNames()
    {
        var members = CreateMembers("张三", "李四", "王五");
        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(
                ExcludeRoundDrawnNames: new HashSet<string> { "张三" }));

        Assert.Equal(["李四", "王五"], result.Select(member => member.Name).ToList());
    }

    [Fact]
    public void Apply_EmptyRoundDrawnSet_DoesNotExclude()
    {
        var members = CreateMembers("张三", "李四", "王五");
        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(
                ExcludeRoundDrawnNames: new HashSet<string>()));

        Assert.Equal(["张三", "李四", "王五"], result.Select(member => member.Name).ToList());
    }

    [Fact]
    public void Apply_ExcludesAbsentMembers()
    {
        var members = CreateMembers("张三", "李四", "王五");
        members[1].AbsentOn = DateOnly.FromDateTime(DateTime.Today);

        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(
                ExcludeAbsentNames: DrawFilterService.GetAbsentNames(members)));

        Assert.Equal(["张三", "王五"], result.Select(member => member.Name).ToList());
    }

    [Fact]
    public void Apply_AbsentExpiresNextDay()
    {
        var members = CreateMembers("张三");
        members[0].AbsentOn = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)); // 昨天的请假

        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(
                ExcludeAbsentNames: DrawFilterService.GetAbsentNames(members)));

        Assert.Single(result); // 昨天请假的成员今天正常参与
    }

    [Fact]
    public void ComputeRoundProgress_CountsDrawnAndDrawable()
    {
        var members = CreateMembers("张三", "李四", "王五");
        var roundDrawn = new HashSet<string> { "张三", "李四" };
        var absent = DrawFilterService.GetAbsentNames(members);

        var progress = DrawFilterService.ComputeRoundProgress(
            members,
            [],
            [],
            new DrawFilterOptions(),
            roundDrawn,
            absent);

        Assert.Equal(2, progress.DrawnCount);
        Assert.Equal(1, progress.DrawableCount);
        Assert.Equal(3, progress.TotalCount);
        Assert.False(progress.NeedsNextRound);
    }

    [Fact]
    public void ComputeRoundProgress_NeedsNextRoundWhenPoolExhausted()
    {
        var members = CreateMembers("张三", "李四");
        var roundDrawn = new HashSet<string> { "张三", "李四" };

        var progress = DrawFilterService.ComputeRoundProgress(
            members,
            [],
            [],
            new DrawFilterOptions(),
            roundDrawn,
            new HashSet<string>());

        Assert.True(progress.NeedsNextRound);
        Assert.Equal(0, progress.DrawableCount);
    }

    [Fact]
    public void RoundState_StartNextRound_ClearsDrawnNamesAndIncrements()
    {
        var round = new RoundState { RoundNumber = 3 };
        round.DrawnNames.Add("张三");

        round.StartNextRound();

        Assert.Equal(4, round.RoundNumber);
        Assert.Empty(round.DrawnNames);
        Assert.NotNull(round.StartedAt);
    }
}
