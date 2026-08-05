using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IImplicitWeightProvider
{
    Task<ImplicitWeightLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使配置文件与当前成员名单精确一致。<paramref name="renamedMembers"/> 的键为旧姓名，值为新姓名，
    /// 可让改名后的成员继承原有配置权重。
    /// </summary>
    Task<ImplicitWeightLoadResult> SynchronizeAsync(
        IReadOnlyCollection<Member> members,
        IReadOnlyDictionary<string, string>? renamedMembers = null,
        CancellationToken cancellationToken = default);
}
