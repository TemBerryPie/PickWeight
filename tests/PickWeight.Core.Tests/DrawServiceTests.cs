using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class DrawServiceTests
{
    [Fact]
    public void Draw_IsWithoutReplacement_AndDoesNotMutateMembers()
    {
        var members = CreateMembers("A", "B", "C");
        members[0].ExplicitWeight = 250d;
        members[0].CalledCount = 3;
        members[0].IsWeightLinked = false;
        var before = members.Select(member => member.Clone()).ToList();

        var result = new DrawService(() => 0d).Draw(members, [], 3, new DrawOptions(DrawMode.SmartRandom));

        Assert.Equal(3, result.SelectedMembers.Select(member => member.Name).Distinct().Count());
        Assert.Equal(before.Select(member => member.CalledCount), members.Select(member => member.CalledCount));
        Assert.Equal(before.Select(member => member.ExplicitWeight), members.Select(member => member.ExplicitWeight));
        Assert.Equal(before.Select(member => member.IsWeightLinked), members.Select(member => member.IsWeightLinked));
    }

    [Fact]
    public void Preview_WeightedAndSelectionUseExactSeventyThirtyBlend()
    {
        var members = CreateMembers("ConfigFavorite", "ModeFavorite");
        members[0].ImplicitWeight = 9d;
        members[0].ExplicitWeight = 1d;
        members[1].ImplicitWeight = 1d;
        members[1].ExplicitWeight = 3d;
        var service = new DrawService(() => 0d);

        foreach (var mode in new[] { DrawMode.Weighted, DrawMode.Selection })
        {
            var preview = service.PreviewProbabilities(members, [], new DrawOptions(mode));

            Assert.Equal(0.705d, Find(preview, "ConfigFavorite").Probability, 12);
            Assert.Equal(0.295d, Find(preview, "ModeFavorite").Probability, 12);
        }
    }

    [Fact]
    public void Preview_SmartRandomUsesCalledWeightForItsThirtyPercentOnly()
    {
        var members = CreateMembers("ConfigFavorite", "FreshCandidate");
        members[0].ImplicitWeight = 9d;
        members[0].CalledCount = 2;
        members[0].ExplicitWeight = 1_000_000d;
        members[1].ImplicitWeight = 1d;
        members[1].CalledCount = 0;
        members[1].ExplicitWeight = 0d;

        var preview = new DrawService(() => 0d)
            .PreviewProbabilities(members, [], new DrawOptions(DrawMode.SmartRandom));

        Assert.Equal(0.7d * 0.9d + 0.3d * (5d / 11d), Find(preview, "ConfigFavorite").Probability, 12);
        Assert.Equal(0.7d * 0.1d + 0.3d * (6d / 11d), Find(preview, "FreshCandidate").Probability, 12);
    }

    [Fact]
    public void Preview_FullRandomUsesEqualModeWeightForItsThirtyPercentOnly()
    {
        var members = CreateMembers("ConfigFavorite", "Other");
        members[0].ImplicitWeight = 9d;
        members[0].ExplicitWeight = 0d;
        members[0].CalledCount = 100;
        members[1].ImplicitWeight = 1d;
        members[1].ExplicitWeight = 1_000_000d;

        var preview = new DrawService(() => 0d)
            .PreviewProbabilities(members, [], new DrawOptions(DrawMode.FullRandom));

        Assert.Equal(0.78d, Find(preview, "ConfigFavorite").Probability, 12);
        Assert.Equal(0.22d, Find(preview, "Other").Probability, 12);
    }

    [Fact]
    public void WeightedMode_UsesConfigurationShareEvenWhenExplicitWeightIsZero()
    {
        var members = CreateMembers("ZeroExplicit", "HighExplicit");
        members[0].ExplicitWeight = 0d;
        members[1].ExplicitWeight = 100d;

        var preview = new DrawService(() => 0d).PreviewProbabilities(members, []);

        Assert.Equal(0.35d, Find(preview, "ZeroExplicit").Probability, 12);
        Assert.Equal(0.65d, Find(preview, "HighExplicit").Probability, 12);
    }

    [Theory]
    [InlineData(DrawMode.Weighted)]
    [InlineData(DrawMode.Selection)]
    [InlineData(DrawMode.SmartRandom)]
    [InlineData(DrawMode.FullRandom)]
    public void Draw_AllModesApplyTriggeredGroupMultiplier(DrawMode mode)
    {
        var members = CreateMembers("First", "GroupMate", "Other");
        var groups = new[]
        {
            new Group
            {
                GroupName = "Team",
                Members = ["First", "GroupMate"],
                SameDrawBoostMultiplier = 4d
            }
        };
        var points = new Queue<double>([0d, 0.7d]);

        var result = new DrawService(() => points.Dequeue())
            .Draw(members, groups, 2, new DrawOptions(mode));

        Assert.Equal(["First", "GroupMate"], result.SelectedMembers.Select(member => member.Name));
        Assert.Equal(["Team"], result.TriggeredGroups);
    }

    [Fact]
    public void Draw_DisabledGroupDoesNotBoostLaterMember()
    {
        var members = CreateMembers("First", "DisabledMate", "Other");
        var groups = new[]
        {
            new Group { GroupName = "Disabled", Members = ["First", "DisabledMate"], IsDrawBoostEnabled = false }
        };
        var points = new Queue<double>([0d, 0.55d]);

        var result = new DrawService(() => points.Dequeue()).Draw(members, groups, 2);

        Assert.Equal(["First", "Other"], result.SelectedMembers.Select(member => member.Name));
        Assert.Empty(result.TriggeredGroups!);
    }

    [Fact]
    public void Draw_ReportsVisibleWeightsWithoutExposingConfigurationWeight()
    {
        var members = CreateMembers("A");
        members[0].ExplicitWeight = 250d;
        members[0].ImplicitWeight = 9d;
        members[0].CalledCount = 2;

        var result = new DrawService(() => 0d).Draw(members, [], 1);

        var weights = Assert.Single(result.SelectedWeights!);
        Assert.Equal("A", weights.Name);
        Assert.Equal(250d, weights.ExplicitWeight);
        Assert.Equal(MemberRules.CalledWeight(2), weights.CalledWeight);
        Assert.DoesNotContain(
            typeof(DrawnMemberWeight).GetProperties().Select(property => property.Name),
            name => name.Contains("Implicit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, members[0].CalledCount);
        Assert.Equal(250d, members[0].ExplicitWeight);
    }

    [Fact]
    public void Preview_DoesNotExposeConfigurationWeight()
    {
        var previewProperties = typeof(DrawProbabilityPreview).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(previewProperties, name => name.Contains("Implicit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(previewProperties, name => name.Contains("Config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutoReset_ResetsWeightsAndCountsWhenIntervalIsReached()
    {
        var members = new List<Member>
        {
            new() { Name = "A", ExplicitWeight = 250d, CalledCount = 4, IsWeightLinked = false },
            new() { Name = "B", ExplicitWeight = 80d, CalledCount = 2, IsWeightLinked = false }
        };
        var settings = new AppSettings
        {
            AutoResetWeights = true,
            AutoResetDrawInterval = 2,
            DrawsSinceLastAutoReset = 1
        };

        var reset = AutoResetService.RecordSuccessfulDraw(settings, members);

        Assert.True(reset);
        Assert.All(members, member =>
        {
            Assert.Equal(100d, member.ExplicitWeight);
            Assert.Equal(0, member.CalledCount);
            Assert.True(member.IsWeightLinked);
        });
        Assert.Equal(0, settings.DrawsSinceLastAutoReset);
    }

    private static DrawProbabilityPreview Find(IEnumerable<DrawProbabilityPreview> preview, string name) =>
        Assert.Single(preview.Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));

    private static List<Member> CreateMembers(params string[] names) =>
        names.Select(name => new Member { Name = name }).ToList();
}
