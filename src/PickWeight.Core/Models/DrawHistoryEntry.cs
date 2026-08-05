namespace PickWeight.Core.Models;

public sealed class DrawHistoryEntry
{
    public DateTimeOffset DrawnAt { get; set; }
    public List<string> MemberNames { get; set; } = [];

    public DrawHistoryEntry Clone() => new()
    {
        DrawnAt = DrawnAt,
        MemberNames = [.. MemberNames]
    };
}
