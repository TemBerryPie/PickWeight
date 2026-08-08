using System.IO.Pipes;
using System.Text.Json;

namespace PickWeight.ClassIsland.Protocol;

public static class ClassIslandProtocol
{
    /// <summary>当前协议版本。v2 新增课时上下文查询，v1 客户端仍可收发结果通知。</summary>
    public const int Version = 2;
    public const string DrawResultType = "draw-result";
    public const string ContextQueryType = "context-query";
    public const string PipeName = "PickWeight.ClassIsland.Notification.v1";
    public const int MaximumMessageBytes = 16 * 1024;
    public const int MaximumNameCount = 100;
    public const int MaximumNameLength = 100;

    /// <summary>版本不匹配时的错误码，用于主程序提示用户升级插件。</summary>
    public const string VersionMismatchError = "VERSION_MISMATCH";

    /// <summary>与协议一致的 JSON 序列化选项（camelCase、大小写不敏感）。</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static DrawResultRequest CreateDrawResultRequest(IEnumerable<string> names) =>
        new(Version, DrawResultType, names.Select(name => name.Trim()).ToArray());

    public static ContextQueryRequest CreateContextQueryRequest() =>
        new(Version, ContextQueryType);

    public static string FormatDrawResult(IEnumerable<string> names) =>
        $"{string.Join("、", names)}被抽中";

    /// <summary>校验 v1 结果通知请求。版本不匹配时返回专用错误码。</summary>
    public static string? Validate(DrawResultRequest? request)
    {
        if (request is null)
        {
            return "请求为空。";
        }

        if (request.Version != Version)
        {
            return VersionMismatchError;
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

    /// <summary>校验 v2 课时上下文查询请求。</summary>
    public static string? Validate(ContextQueryRequest? request)
    {
        if (request is null)
        {
            return "请求为空。";
        }

        if (request.Version != Version)
        {
            return VersionMismatchError;
        }

        if (!string.Equals(request.Type, ContextQueryType, StringComparison.Ordinal))
        {
            return "不支持的请求类型。";
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

    /// <summary>
    /// 读取一条完整消息（长度前缀 + 负载），返回负载字节与其中的 Type 字段。
    /// 供服务端判断消息类别后，用同一负载反序列化具体类型。
    /// </summary>
    public static async Task<RequestEnvelope> ReadRequestEnvelopeAsync(
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
        using var document = JsonDocument.Parse(payload);
        var type = document.RootElement.TryGetProperty("Type", out var typeProp)
            ? typeProp.GetString()
            : document.RootElement.TryGetProperty("type", out var lowerTypeProp)
                ? lowerTypeProp.GetString()
                : null;
        return new RequestEnvelope(type, payload);
    }
}

/// <summary>已读取的请求信封：消息类别与原始负载。</summary>
public sealed record RequestEnvelope(string? Type, byte[] Payload)
{
    /// <summary>用与协议一致的 JSON 选项（camelCase、大小写不敏感）反序列化负载。</summary>
    public T? Deserialize<T>() =>
        JsonSerializer.Deserialize<T>(Payload, ClassIslandProtocol.JsonOptions);
}

public sealed record DrawResultRequest(int Version, string Type, IReadOnlyList<string> Names);

/// <summary>v2：主程序向插件查询当前课时上下文。</summary>
public sealed record ContextQueryRequest(int Version, string Type);

/// <summary>v2：课时上下文响应。无当前课时时各字段为 null。</summary>
public sealed record ClassContext(
    string? Subject,
    int? PeriodIndex,
    bool IsOnClass,
    DateTimeOffset? PeriodEndsAt);

public sealed record ClassIslandResponse(bool Ok, string? Error = null);

public sealed record ClassIslandSendResult(bool Succeeded, string? Error = null);

/// <summary>课时上下文查询结果。</summary>
public sealed record ClassContextResult(bool Succeeded, ClassContext? Context, string? Error = null)
{
    /// <summary>版本不匹配（插件过旧或过新），需要提示用户升级。</summary>
    public bool IsVersionMismatch => Error == ClassIslandProtocol.VersionMismatchError;
}

public sealed class ClassIslandPipeClient
{
    private readonly TimeSpan _timeout;
    private readonly string _pipeName;

    public ClassIslandPipeClient(TimeSpan? timeout = null, string? pipeName = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(1);
        _pipeName = pipeName ?? ClassIslandProtocol.PipeName;
    }

    /// <summary>向插件查询当前课时上下文。连接失败返回 Succeeded=false；版本不匹配返回专用错误。</summary>
    public async Task<ClassContextResult> QueryContextAsync(
        CancellationToken cancellationToken = default)
    {
        var request = ClassIslandProtocol.CreateContextQueryRequest();
        var validationError = ClassIslandProtocol.Validate(request);
        if (validationError is not null)
        {
            return new ClassContextResult(false, null, validationError);
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
            var response = await ClassIslandProtocol.ReadAsync<ClassContextResponse>(pipe, timeout.Token);
            return response is null
                ? new ClassContextResult(false, null, "ClassIsland 插件未响应。")
                : response.Ok
                    ? new ClassContextResult(true, response.Context)
                    : new ClassContextResult(false, null, response.Error);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException
                                   or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return new ClassContextResult(false, null, ex.Message);
        }
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

/// <summary>v2：插件对课时上下文查询的响应。</summary>
public sealed record ClassContextResponse(bool Ok, ClassContext? Context, string? Error = null);
