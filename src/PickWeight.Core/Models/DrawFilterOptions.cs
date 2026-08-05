namespace PickWeight.Core.Models;

public sealed record DrawFilterOptions(
    string? IncludeGroupName = null,
    string? ExcludeGroupName = null,
    int ExcludeRecentDrawCount = 0,
    bool OnlyNeverDrawn = false);
