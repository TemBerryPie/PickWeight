using System.Text.Json;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

/// <summary>
/// 管理不在 UI 中展示的配置权重文件。文件内容始终由当前成员名单派生，
/// 避免新增、改名或删除成员后留下过期配置。
/// </summary>
public sealed class JsonImplicitWeightProvider : IImplicitWeightProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _preferredPath;
    private readonly string _fallbackPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonImplicitWeightProvider(string preferredPath, string? fallbackPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredPath);
        _preferredPath = preferredPath;
        _fallbackPath = string.IsNullOrWhiteSpace(fallbackPath) ? preferredPath : fallbackPath;
    }

    public async Task<ImplicitWeightLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var location = await ResolveConfigPathAsync(cancellationToken);
            var read = await ReadWeightsAsync(location.FilePath, cancellationToken);
            return new ImplicitWeightLoadResult(read.Weights, location.WasCreated, read.InvalidEntryCount, location.FilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImplicitWeightLoadResult> SynchronizeAsync(
        IReadOnlyCollection<Member> members,
        IReadOnlyDictionary<string, string>? renamedMembers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var location = await ResolveConfigPathAsync(cancellationToken);
            var read = await ReadWeightsAsync(location.FilePath, cancellationToken);
            var currentMembers = NormalizeMembers(members);
            var oldNamesByNewName = BuildOldNamesByNewName(renamedMembers);
            var synchronizedWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in currentMembers)
            {
                if (!read.Weights.TryGetValue(member.NormalizedName, out var weight))
                {
                    weight = FindRenamedWeight(member.NormalizedName, oldNamesByNewName, read.Weights) ?? 1d;
                }

                synchronizedWeights[member.DisplayName] = weight;
            }

            await WriteWeightsAtomicallyAsync(location.FilePath, synchronizedWeights, cancellationToken);
            return new ImplicitWeightLoadResult(
                synchronizedWeights,
                location.WasCreated,
                read.InvalidEntryCount,
                location.FilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string FilePath, bool WasCreated)> ResolveConfigPathAsync(CancellationToken cancellationToken)
    {
        foreach (var path in CandidatePaths())
        {
            if (File.Exists(path))
            {
                return (path, false);
            }
        }

        return (await CreateConfigAsync(cancellationToken), true);
    }

    private IEnumerable<string> CandidatePaths() =>
        new[] { _preferredPath, _fallbackPath }.Distinct(StringComparer.OrdinalIgnoreCase);

    private async Task<string> CreateConfigAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var paths = CandidatePaths().ToArray();
        foreach (var path in paths)
        {
            try
            {
                await WriteWeightsAtomicallyAsync(
                    path,
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
                return path;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastException = exception;
            }
            catch (IOException exception)
            {
                lastException = exception;
            }
        }

        throw new IOException("无法创建配置文件。", lastException);
    }

    private static async Task<WeightReadResult> ReadWeightsAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new WeightReadResult(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), 1);
            }

            var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var invalidEntryCount = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var normalizedName = Normalize(property.Name);
                if (normalizedName.Length == 0
                    || property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetDouble(out var weight)
                    || !IsValidWeight(weight)
                    || !weights.TryAdd(normalizedName, weight))
                {
                    invalidEntryCount++;
                }
            }

            return new WeightReadResult(weights, invalidEntryCount);
        }
        catch (JsonException)
        {
            // 空文件、截断文件或手工编辑错误不会阻塞应用；下一次同步会重建为有效配置。
            return new WeightReadResult(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), 1);
        }
        catch (FileNotFoundException)
        {
            // 文件可能在 File.Exists 和读取之间被替换；同步操作会用当前成员重新创建它。
            return new WeightReadResult(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), 0);
        }
    }

    private static IReadOnlyList<NormalizedMember> NormalizeMembers(IEnumerable<Member> members)
    {
        var result = new List<NormalizedMember>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (member is null)
            {
                continue;
            }

            var displayName = Normalize(member.Name);
            if (displayName.Length > 0 && names.Add(displayName))
            {
                result.Add(new NormalizedMember(displayName, displayName));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildOldNamesByNewName(
        IReadOnlyDictionary<string, string>? renamedMembers)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (renamedMembers is null)
        {
            return result;
        }

        foreach (var pair in renamedMembers)
        {
            var oldName = Normalize(pair.Key);
            var newName = Normalize(pair.Value);
            if (oldName.Length == 0 || newName.Length == 0)
            {
                continue;
            }

            if (!result.TryGetValue(newName, out var oldNames))
            {
                oldNames = [];
                result[newName] = oldNames;
            }

            if (!oldNames.Contains(oldName, StringComparer.OrdinalIgnoreCase))
            {
                result[newName] = [.. oldNames, oldName];
            }
        }

        return result;
    }

    private static double? FindRenamedWeight(
        string newName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> oldNamesByNewName,
        IReadOnlyDictionary<string, double> storedWeights)
    {
        if (!oldNamesByNewName.TryGetValue(newName, out var oldNames))
        {
            return null;
        }

        foreach (var oldName in oldNames)
        {
            if (storedWeights.TryGetValue(oldName, out var weight))
            {
                return weight;
            }
        }

        return null;
    }

    private static async Task WriteWeightsAtomicallyAsync(
        string filePath,
        IReadOnlyDictionary<string, double> weights,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
            filePath = Path.Combine(directory, filePath);
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, weights, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValidWeight(double weight) =>
        double.IsFinite(weight) && weight > 0d && weight <= MemberRules.MaximumWeight;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private sealed record WeightReadResult(
        IReadOnlyDictionary<string, double> Weights,
        int InvalidEntryCount);

    private sealed record NormalizedMember(string DisplayName, string NormalizedName);
}
