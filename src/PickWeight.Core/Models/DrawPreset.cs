namespace PickWeight.Core.Models;

/// <summary>A named, persisted snapshot of the existing draw-rule settings.</summary>
public sealed class DrawPreset
{
    public string Name { get; set; } = string.Empty;
    public DrawMode DrawMode { get; set; } = DrawMode.Weighted;
    public int TargetCount { get; set; } = 1;
    public string? IncludeGroupName { get; set; }
    public string? ExcludeGroupName { get; set; }
    public int ExcludeRecentDrawCount { get; set; }
    public bool OnlyNeverDrawn { get; set; }

    public DrawPreset Clone() => (DrawPreset)MemberwiseClone();
}
