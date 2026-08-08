using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

/// <summary>
/// 全局状态与各班级数据的持久化入口。班级数据按文件夹隔离，文件夹名即班级名。
/// </summary>
public interface IStateStore
{
    Task<AppState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppState state, CancellationToken cancellationToken = default);

    /// <summary>读取指定班级。文件不存在时返回一个同名的空班级。</summary>
    Task<Classroom> LoadClassroomAsync(string name, CancellationToken cancellationToken = default);

    Task SaveClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default);

    /// <summary>扫描磁盘上实际存在的班级文件夹。</summary>
    Task<IReadOnlyList<string>> ListClassroomNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>创建班级文件夹及其初始数据文件。</summary>
    Task CreateClassroomAsync(Classroom classroom, CancellationToken cancellationToken = default);

    /// <summary>重命名班级文件夹。失败时保持原状，不会留下半个状态。</summary>
    Task RenameClassroomAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>删除整个班级文件夹，包含历史与隐式权重配置。</summary>
    Task DeleteClassroomAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>取得班级数据文件夹的绝对路径，隐式权重配置也存放在此。</summary>
    string GetClassroomDirectory(string name);
}
