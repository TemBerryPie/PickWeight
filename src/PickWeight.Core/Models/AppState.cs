namespace PickWeight.Core.Models;

public sealed class AppState
{
    public List<Member> Members { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<DrawHistoryEntry> DrawHistory { get; set; } = [];
    public List<DrawPreset> DrawPresets { get; set; } = [];
    public AppSettings Settings { get; set; } = new();

    public AppState Clone() => new()
    {
        Members = Members.Select(member => member.Clone()).ToList(),
        Groups = Groups.Select(group => group.Clone()).ToList(),
        DrawHistory = DrawHistory.Select(entry => entry.Clone()).ToList(),
        DrawPresets = DrawPresets.Select(preset => preset.Clone()).ToList(),
        Settings = Settings.Clone()
    };
}
