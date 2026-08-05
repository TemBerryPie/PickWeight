namespace PickWeight.Core.Models;

public sealed record HistoryFilterOptions(
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null,
    string? MemberName = null,
    string? GroupName = null);
