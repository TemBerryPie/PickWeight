using System.Security.Cryptography;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 仅负责计算抽取结果和概率。提交被点次数、历史记录及通知由上层执行服务负责，
/// 这样预览、主窗口和快捷抽取窗口可以共享完全相同的计算逻辑而不产生副作用。
/// </summary>
public sealed class DrawService : IDrawService
{
    private const double ImplicitWeightShare = 0.7d;
    private const double ModeWeightShare = 0.3d;
    private readonly Func<double> _nextUnitDouble;

    public DrawService() : this(CreateSecureUnitDouble)
    {
    }

    /// <summary>
    /// 可注入随机源用于单元测试；生产环境默认使用安全随机数生成器。
    /// </summary>
    public DrawService(Func<double> nextUnitDouble)
    {
        _nextUnitDouble = nextUnitDouble ?? throw new ArgumentNullException(nameof(nextUnitDouble));
    }

    public DrawResult Draw(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        int targetCount,
        DrawOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(groups);

        if (targetCount <= 0)
        {
            return new DrawResult([], 0, "抽取人数必须大于 0。");
        }

        var mode = options?.Mode ?? DrawMode.Weighted;
        var candidates = members
            .Where(member => IsEligible(member, mode))
            .DistinctBy(member => Normalize(member.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var eligibleCount = candidates.Count;
        var selected = new List<Member>();
        var selectedWeights = new List<DrawnMemberWeight>();
        var triggeredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memberGroups = BuildMemberGroups(groups);

        while (selected.Count < targetCount && candidates.Count > 0)
        {
            var probabilities = CalculateProbabilities(candidates, mode, memberGroups, triggeredGroups);
            var probabilityTotal = probabilities.Sum(item => item.Score);
            if (probabilityTotal <= 0d || !double.IsFinite(probabilityTotal))
            {
                break;
            }

            var point = Math.Clamp(_nextUnitDouble(), 0d, Math.BitDecrement(1d));
            var running = 0d;
            var winner = probabilities[^1].Member;
            foreach (var item in probabilities)
            {
                running += item.Score / probabilityTotal;
                if (point < running)
                {
                    winner = item.Member;
                    break;
                }
            }

            candidates.Remove(winner);
            selectedWeights.Add(new DrawnMemberWeight(
                winner.Name,
                winner.ExplicitWeight,
                MemberRules.CalledWeight(winner.CalledCount)));
            selected.Add(winner);

            if (memberGroups.TryGetValue(Normalize(winner.Name), out var winnerGroups))
            {
                triggeredGroups.UnionWith(winnerGroups.Keys);
            }
        }

        var message = selected.Count < targetCount
            ? $"有效候选人数不足，已抽取 {selected.Count} 人。"
            : null;
        return new DrawResult(selected, eligibleCount, message, triggeredGroups.Order().ToList(), selectedWeights);
    }

    public IReadOnlyList<DrawProbabilityPreview> PreviewProbabilities(
        IReadOnlyList<Member> members,
        IReadOnlyList<Group> groups,
        DrawOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(groups);

        var mode = options?.Mode ?? DrawMode.Weighted;
        var candidates = members
            .Where(member => IsEligible(member, mode))
            .DistinctBy(member => Normalize(member.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var probabilities = CalculateProbabilities(
            candidates,
            mode,
            BuildMemberGroups(groups),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var total = probabilities.Sum(item => item.Score);
        if (total <= 0d || !double.IsFinite(total))
        {
            return [];
        }

        return probabilities
            .Select(item => new DrawProbabilityPreview(
                item.Member.Name,
                item.Score / total,
                item.Score,
                item.Member.ExplicitWeight,
                MemberRules.CalledWeight(item.Member.CalledCount),
                item.Member.CalledCount,
                item.GroupMultiplier))
            .OrderByDescending(item => item.Probability)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ProbabilityItem> CalculateProbabilities(
        IReadOnlyList<Member> candidates,
        DrawMode mode,
        IReadOnlyDictionary<string, Dictionary<string, double>> memberGroups,
        IReadOnlySet<string> triggeredGroups)
    {
        var weightedCandidates = candidates
            .Select(member => new CandidateWeight(
                member,
                GetModeWeight(member, mode),
                GetImplicitWeight(member)))
            .Where(item => double.IsFinite(item.ModeWeight)
                && item.ModeWeight >= 0d
                && (item.ImplicitWeight is not null || item.ModeWeight > 0d))
            .ToList();

        if (weightedCandidates.Count == 0)
        {
            return [];
        }

        var modeTotal = weightedCandidates.Sum(item => item.ModeWeight);
        var implicitTotal = weightedCandidates
            .Where(item => item.ImplicitWeight is not null)
            .Sum(item => item.ImplicitWeight!.Value);
        if (!double.IsFinite(modeTotal) || !double.IsFinite(implicitTotal))
        {
            return [];
        }

        return weightedCandidates
            .Select(item =>
            {
                var modeProbability = modeTotal > 0d
                    ? item.ModeWeight / modeTotal
                    : 0d;
                var implicitProbability = item.ImplicitWeight is not null && implicitTotal > 0d
                    ? item.ImplicitWeight.Value / implicitTotal
                    : 0d;

                // 所有正常成员按 70% 配置权重 + 30% 当前模式评分混合。
                // 极少数尚未同步到配置的成员不应被静默排除，保留其完整模式评分作为安全回退。
                var score = item.ImplicitWeight is null
                    ? modeProbability
                    : (implicitProbability * ImplicitWeightShare) + (modeProbability * ModeWeightShare);
                var groupMultiplier = CalculateGroupMultiplier(item.Member, memberGroups, triggeredGroups);
                return new ProbabilityItem(item.Member, score * groupMultiplier, groupMultiplier);
            })
            .Where(item => item.Score > 0d && double.IsFinite(item.Score))
            .ToList();
    }

    private static double GetModeWeight(Member member, DrawMode mode) => mode switch
    {
        DrawMode.FullRandom => 1d,
        DrawMode.SmartRandom => MemberRules.CalledWeight(member.CalledCount),
        DrawMode.Selection or DrawMode.Weighted => member.ExplicitWeight * MemberRules.CalledWeight(member.CalledCount),
        _ => 0d
    };

    private static double? GetImplicitWeight(Member member) =>
        double.IsFinite(member.ImplicitWeight) && member.ImplicitWeight > 0d && member.ImplicitWeight <= MemberRules.MaximumWeight
            ? member.ImplicitWeight
            : null;

    private static bool IsEligible(Member member, DrawMode mode)
    {
        if (!member.IsActive || string.IsNullOrWhiteSpace(member.Name) || member.CalledCount < 0)
        {
            return false;
        }

        if ((mode is DrawMode.Weighted or DrawMode.Selection)
            && (!double.IsFinite(member.ExplicitWeight) || member.ExplicitWeight < 0d))
        {
            return false;
        }

        return GetImplicitWeight(member) is not null || GetModeWeight(member, mode) > 0d;
    }

    private static double CalculateGroupMultiplier(
        Member member,
        IReadOnlyDictionary<string, Dictionary<string, double>> memberGroups,
        IReadOnlySet<string> triggeredGroups)
    {
        if (!memberGroups.TryGetValue(Normalize(member.Name), out var groups))
        {
            return 1d;
        }

        return groups
            .Where(group => triggeredGroups.Contains(group.Key))
            .Aggregate(1d, (multiplier, group) => Math.Min(multiplier * group.Value, 100d));
    }

    private static Dictionary<string, Dictionary<string, double>> BuildMemberGroups(IEnumerable<Group> groups)
    {
        var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.Where(group => group.IsDrawBoostEnabled && !string.IsNullOrWhiteSpace(group.GroupName)))
        {
            var multiplier = Math.Clamp(
                double.IsFinite(group.SameDrawBoostMultiplier) ? group.SameDrawBoostMultiplier : 1d,
                1d,
                10d);
            foreach (var memberName in group.Members)
            {
                var normalized = Normalize(memberName);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!result.TryGetValue(normalized, out var memberGroupNames))
                {
                    memberGroupNames = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    result[normalized] = memberGroupNames;
                }

                memberGroupNames[Normalize(group.GroupName)] = multiplier;
            }
        }

        return result;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private sealed record CandidateWeight(Member Member, double ModeWeight, double? ImplicitWeight);
    private sealed record ProbabilityItem(Member Member, double Score, double GroupMultiplier);

    private static double CreateSecureUnitDouble()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes) >> 11;
        return value * (1d / (1UL << 53));
    }
}
