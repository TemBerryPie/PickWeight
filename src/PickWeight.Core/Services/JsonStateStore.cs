using System.Text.Json;
using System.Text.Json.Serialization;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public sealed class JsonStateStore : IStateStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonStateStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AppState();
        }

        await using var stream = File.OpenRead(_filePath);
        var state = await JsonSerializer.DeserializeAsync<AppState>(stream, _options, cancellationToken)
            ?? new AppState();

        state.Members ??= [];
        state.Groups ??= [];
        state.Settings ??= new AppSettings();
        state.DrawHistory ??= [];
        state.DrawPresets ??= [];
        foreach (var member in state.Members)
        {
            member.Name = member.Name.Trim();
            member.ImplicitWeight = 1d;
            MemberRules.Validate(member);
        }
        foreach (var entry in state.DrawHistory)
        {
            entry.MemberNames ??= [];
        }
        return state;
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        foreach (var member in state.Members)
        {
            MemberRules.Validate(member);
        }

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("状态文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, _options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }
}
