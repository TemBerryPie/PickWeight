namespace PickWeight.Core.Models;

/// <summary>
/// 轮转抽取状态。开启「一轮不重复」后，本轮已抽到的成员会被排除，
/// 直到候选池抽空自动进入下一轮。
/// </summary>
public sealed class RoundState
{
    public int RoundNumber { get; set; } = 1;

    /// <summary>本轮已被抽中的成员姓名。</summary>
    public List<string> DrawnNames { get; set; } = [];

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>结束当前轮次并开始新一轮。</summary>
    public void StartNextRound()
    {
        RoundNumber++;
        DrawnNames.Clear();
        StartedAt = DateTimeOffset.Now;
    }

    /// <summary>本轮已抽到的姓名集合（规范化后）。</summary>
    public IReadOnlySet<string> DrawnNamesSet() =>
        DrawnNames.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public RoundState Clone() => new()
    {
        RoundNumber = RoundNumber,
        DrawnNames = [.. DrawnNames],
        StartedAt = StartedAt
    };
}
