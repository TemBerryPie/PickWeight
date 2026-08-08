namespace PickWeight.Core.Abstractions;

/// <summary>
/// 按班级文件夹创建隐式权重提供者。每个班级的 implicit_config.json
/// 与该班级的 classroom.json 放在同一个文件夹内，切换班级时需要换用对应的提供者。
/// </summary>
public interface IImplicitWeightProviderFactory
{
    /// <summary>
    /// 为指定班级文件夹创建提供者。
    /// </summary>
    /// <param name="classroomDirectory">班级文件夹的绝对路径，文件夹名即班级名。</param>
    IImplicitWeightProvider Create(string classroomDirectory);
}
