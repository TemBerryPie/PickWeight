using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class MemberRules
{
    public const double DefaultExplicitWeight = 100d;
    public const double MaximumWeight = 1_000_000d;

    public static double CalledWeight(int calledCount) =>
        1d / (1d + Math.Max(0, calledCount) * 0.1d);

    /// <summary>
    /// 编辑器手动调整被点次数。显式权重是用户输入，不会随次数发生二次衰减。
    /// </summary>
    public static void ChangeCalledCount(Member member, int newCount)
    {
        ArgumentNullException.ThrowIfNull(member);
        member.CalledCount = Math.Max(0, newCount);
    }

    public static void ChangeSmartRandomCalledCount(Member member, int newCount)
    {
        ChangeCalledCount(member, newCount);
    }

    public static void IncrementCalledCount(Member member, DrawMode mode)
    {
        ChangeCalledCount(member, member.CalledCount + 1);
    }

    public static void ApplySmartRandomDefaults(IEnumerable<Member> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        foreach (var member in members)
        {
            ApplySmartRandomDefaults(member);
        }
    }

    public static void ApplySmartRandomDefaults(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        // 兼容旧调用点：智能随机只改变抽取评分规则，不应覆盖用户编辑的显式权重。
    }

    public static void SetExplicitWeight(Member member, double weight)
    {
        ValidateWeight(weight, nameof(weight));
        member.ExplicitWeight = weight;
        member.IsWeightLinked = false;
    }

    /// <summary>
    /// 课堂表现评价的默认权重倍率。良好下调权重（给其他同学更多机会），
    /// 一般轻微下调权重，未答上与主动回答（已停用）不调整权重。
    /// </summary>
    public static double? RatingMultiplier(DrawRating rating) => rating switch
    {
        DrawRating.Excellent => 0.7d,
        DrawRating.Fair => 0.9d,
        DrawRating.NoAnswer or DrawRating.Volunteered => 1d,
        _ => null
    };

    /// <summary>
    /// 按课堂表现评价调整显式权重，并标记 <see cref="Member.IsWeightAdjustedByRating"/>。
    /// 权重下限为默认权重的 10%，避免评价把成员权重压到无法参与抽取。
    /// </summary>
    public static void ApplyRatingAdjustment(Member member, DrawRating rating, double multiplier)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (rating is not (DrawRating.Excellent or DrawRating.Fair))
        {
            return;
        }

        var normalizedMultiplier = Math.Clamp(
            double.IsFinite(multiplier) ? multiplier : 1d,
            0d,
            MaximumWeight);
        member.ExplicitWeight = Math.Max(
            DefaultExplicitWeight * 0.1d,
            Math.Min(MaximumWeight, member.ExplicitWeight * normalizedMultiplier));
        member.IsWeightAdjustedByRating = true;
    }

    public static void Reset(Member member)
    {
        member.ExplicitWeight = DefaultExplicitWeight;
        member.CalledCount = 0;
        member.IsWeightLinked = true;
        member.IsWeightAdjustedByRating = false;
    }

    public static void Validate(Member member)
    {
        if (string.IsNullOrWhiteSpace(member.Name))
        {
            throw new InvalidDataException("成员姓名不能为空。");
        }

        ValidateWeight(member.ExplicitWeight, nameof(member.ExplicitWeight));
        ValidateWeight(member.ImplicitWeight, nameof(member.ImplicitWeight), allowZero: false);

        if (member.Gender is { } gender && !Enum.IsDefined(gender))
        {
            throw new InvalidDataException("性别值无效。");
        }

        if (member.CalledCount < 0)
        {
            throw new InvalidDataException("被点次数不能小于 0。");
        }
    }

    public static void ValidateWeight(double weight, string name, bool allowZero = true)
    {
        if (!double.IsFinite(weight) || weight < (allowZero ? 0d : double.Epsilon) || weight > MaximumWeight)
        {
            throw new InvalidDataException($"{name} 必须是有效范围内的数值。");
        }
    }
}
