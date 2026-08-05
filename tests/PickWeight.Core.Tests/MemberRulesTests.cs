using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class MemberRulesTests
{
    [Fact]
    public void ManualCountChange_DoesNotChangeExplicitWeight()
    {
        var member = new Member { Name = "A", ExplicitWeight = 250d, IsWeightLinked = true };

        MemberRules.ChangeCalledCount(member, 4);

        Assert.Equal(4, member.CalledCount);
        Assert.Equal(250d, member.ExplicitWeight);
    }

    [Fact]
    public void SmartRandomCountChange_DoesNotOverwriteExplicitWeight()
    {
        var member = new Member
        {
            Name = "A",
            ExplicitWeight = 250d,
            CalledCount = 3,
            IsWeightLinked = false
        };

        MemberRules.ChangeSmartRandomCalledCount(member, 4);
        MemberRules.ApplySmartRandomDefaults(member);

        Assert.Equal(4, member.CalledCount);
        Assert.Equal(250d, member.ExplicitWeight);
        Assert.False(member.IsWeightLinked);
    }

    [Fact]
    public void IncrementCalledCount_OnlyUpdatesCountForEveryMode()
    {
        var member = new Member { Name = "A", ExplicitWeight = 88d, CalledCount = 2 };

        foreach (var mode in Enum.GetValues<DrawMode>())
        {
            MemberRules.IncrementCalledCount(member, mode);
        }

        Assert.Equal(2 + Enum.GetValues<DrawMode>().Length, member.CalledCount);
        Assert.Equal(88d, member.ExplicitWeight);
    }

    [Fact]
    public void Validate_RejectsUnknownGender()
    {
        var member = new Member { Name = "A", Gender = (MemberGender)99 };

        Assert.Throws<InvalidDataException>(() => MemberRules.Validate(member));
    }
}
