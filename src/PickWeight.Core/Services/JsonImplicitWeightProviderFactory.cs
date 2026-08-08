using PickWeight.Core.Abstractions;

namespace PickWeight.Core.Services;

/// <summary>
/// 在班级文件夹下创建 implicit_config.json 的提供者。
/// </summary>
public sealed class JsonImplicitWeightProviderFactory : IImplicitWeightProviderFactory
{
    public const string ConfigFileName = "implicit_config.json";

    private readonly Dictionary<string, IImplicitWeightProvider> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IImplicitWeightProvider Create(string classroomDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classroomDirectory);

        var fullPath = Path.GetFullPath(classroomDirectory);
        lock (_gate)
        {
            if (_cache.TryGetValue(fullPath, out var cached))
            {
                return cached;
            }

            // 提供者内部有写入串行锁，按文件夹缓存可避免同一班级出现并发写入。
            var provider = new JsonImplicitWeightProvider(Path.Combine(fullPath, ConfigFileName));
            _cache[fullPath] = provider;
            return provider;
        }
    }

    /// <summary>
    /// 班级被删除或改名后丢弃缓存的提供者，避免继续指向旧文件夹。
    /// </summary>
    public void Invalidate(string classroomDirectory)
    {
        if (string.IsNullOrWhiteSpace(classroomDirectory))
        {
            return;
        }

        lock (_gate)
        {
            _cache.Remove(Path.GetFullPath(classroomDirectory));
        }
    }
}
