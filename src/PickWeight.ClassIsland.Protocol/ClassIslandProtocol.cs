using System.IO.Pipes;
using System.Text.Json;

namespace PickWeight.ClassIsland.Protocol;

public static class ClassIslandProtocol
{
    public const int Version = 1;
    public const string DrawResultType = "draw-result";
    public const string PipeName = "PickWeight.ClassIsland.Notification.v1";
    public const int MaximumMessageBytes = 16 * 1024;
    public const int MaximumNameCount = 100;
    public const int MaximumNameLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static DrawResultRequest CreateDrawResultRequest(IEnumerable<string> names) =>
        new(Version, DrawResultType, names.Select(name => name.Trim()).ToArray());

    public static string FormatDrawResult(IEnumerable<string> names) =>
        $"{string.Join("、", names)}被抽中";

    public static string? Validate(DrawResultRequest? request)
    {
        if (request is null)
        {
            return "请求为空。";
        }

        if (request.Version != Version)
        {
            return "不支持的协议版本。";
        }

        if (!string.Equals(request.Type, DrawResultType, StringComparison.Ordinal))
        {
            return "不支持的请求类型。";
        }

        if (request.Names is null || request.Names.Count == 0)
        {
            return "名单不能为空。";
        }

        if (request.Names.Count > MaximumNameCount)
        {
            return "名单人数超过限制。";
        }

        if (request.Names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength))
        {
            return "名单包含无效姓名。";
        }

        return null;
    }

    public static async Task WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("消息超过大小限制。");
        }

        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T?> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > MaximumMessageBytes)
        {
            throw new InvalidDataException("消息长度无效。");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }
}

public sealed record DrawResultRequest(int Version, string Type, IReadOnlyList<string> Names);

public sealed record ClassIslandResponse(bool Ok, string? Error = null);

public sealed record ClassIslandSendResult(bool Succeeded, string? Error = null);

public sealed class ClassIslandPipeClient
{
    private readonly TimeSpan _timeout;
    private readonly string _pipeName;

    public ClassIslandPipeClient(TimeSpan? timeout = null, string? pipeName = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _pipeName = pipeName ?? ClassIslandProtocol.PipeName;
    }

    public async Task<ClassIslandSendResult> SendDrawResultAsync(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        var request = ClassIslandProtocol.CreateDrawResultRequest(names);
        var validationError = ClassIslandProtocol.Validate(request);
        if (validationError is not null)
        {
            return new ClassIslandSendResult(false, validationError);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await ClassIslandProtocol.WriteAsync(pipe, request, timeout.Token);
            var response = await ClassIslandProtocol.ReadAsync<ClassIslandResponse>(pipe, timeout.Token);
            return response?.Ok == true
                ? new ClassIslandSendResult(true)
                : new ClassIslandSendResult(false, response?.Error ?? "ClassIsland 插件未确认请求。");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException
                                   or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return new ClassIslandSendResult(false, ex.Message);
        }
    }
}
