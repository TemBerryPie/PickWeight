namespace PickWeight.Core.Models;

/// <summary>轮转抽取的进度快照。</summary>
public sealed record RoundProgress(int DrawnCount, int DrawableCount)
{
    /// <summary>本轮是否已把所有可抽候选抽完，需要翻轮。</summary>
    public bool NeedsNextRound => DrawnCount > 0 && DrawableCount == 0;

    /// <summary>本轮总候选数（已抽 + 剩余可抽）。</summary>
    public int TotalCount => DrawnCount + DrawableCount;
}
