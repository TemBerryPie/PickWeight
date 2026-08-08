using System.Text.Json.Serialization;

namespace PickWeight.Core.Models;

/// <summary>
/// 全局应用状态。名单、小组、历史等按班级存放在各自的文件夹中，
/// 这里只保留跨班级共享的设置和班级索引。
/// </summary>
public sealed class AppState
{
    /// <summary>当前数据结构版本。缺失或为 0 表示 v1 单文件结构，需要迁移。</summary>
    public int SchemaVersion { get; set; }

    public AppSettings Settings { get; set; } = new();

    /// <summary>当前选中的班级名称，同时是其数据文件夹名。</summary>
    public string ActiveClassroomName { get; set; } = Classroom.DefaultName;

    /// <summary>已知的班级名称列表，启动时会与磁盘上的文件夹重新同步。</summary>
    public List<string> ClassroomNames { get; set; } = [];

    /// <summary>当前的数据结构版本号。</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// 迁移失败时为 true，应用以只读模式启动，避免覆盖尚未成功迁移的原始数据。
    /// </summary>
    [JsonIgnore]
    public bool IsReadOnly { get; set; }

    /// <summary>需要展示给用户的启动提示，例如迁移结果或数据异常。</summary>
    [JsonIgnore]
    public string StartupNotice { get; set; } = string.Empty;

    public AppState Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Settings = Settings.Clone(),
        ActiveClassroomName = ActiveClassroomName,
        ClassroomNames = [.. ClassroomNames],
        IsReadOnly = IsReadOnly,
        StartupNotice = StartupNotice
    };
}
