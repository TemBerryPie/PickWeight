namespace PickWeight.Core.Models;

/// <summary>座位抽取区域配置。</summary>
public sealed record SeatFilterOptions(
    SeatRegion Region = SeatRegion.All,
    int ColumnIndex = -1,
    int RowIndex = -1);

public sealed record DrawFilterOptions(
    string? IncludeGroupName = null,
    string? ExcludeGroupName = null,
    int ExcludeRecentDrawCount = 0,
    bool OnlyNeverDrawn = false,
    IReadOnlySet<string>? ExcludeRoundDrawnNames = null,
    IReadOnlySet<string>? ExcludeAbsentNames = null,
    SeatFilterOptions? SeatFilter = null);
