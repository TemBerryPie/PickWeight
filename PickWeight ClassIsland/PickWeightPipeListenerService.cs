using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using PickWeight.ClassIsland.Protocol;

namespace PickWeight.ClassIsland.Plugin;

public sealed class PickWeightPipeListenerService : BackgroundService
{
    private readonly NotificationProviderAccessor _accessor;
    private readonly PickWeightClassContextService _contextService;

    public PickWeightPipeListenerService(
        NotificationProviderAccessor accessor,
        PickWeightClassContextService contextService)
    {
        _accessor = accessor;
        _contextService = contextService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ClassIslandProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleConnectionAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, stoppingToken);
            }
        }
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try
        {
            // 先读取完整信封，判断消息类别后再用同一负载反序列化
            var envelope = await ClassIslandProtocol.ReadRequestEnvelopeAsync(pipe, cancellationToken);
            if (string.Equals(envelope.Type, ClassIslandProtocol.ContextQueryType, StringComparison.Ordinal))
            {
                await HandleContextQueryAsync(pipe, envelope, cancellationToken);
                return;
            }

            if (string.Equals(envelope.Type, ClassIslandProtocol.DrawResultType, StringComparison.Ordinal))
            {
                await HandleDrawResultAsync(pipe, envelope, cancellationToken);
                return;
            }

            await ClassIslandProtocol.WriteAsync(
                pipe,
                new ClassIslandResponse(false, "不支持的请求类型。"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            try
            {
                await ClassIslandProtocol.WriteAsync(
                    pipe,
                    new ClassIslandResponse(false, "请求格式无效。"),
                    cancellationToken);
            }
            catch
            {
                // 连接已断开时忽略
            }
        }
    }

    private async Task HandleContextQueryAsync(Stream pipe, RequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = envelope.Deserialize<ContextQueryRequest>();
        var validationError = ClassIslandProtocol.Validate(request);
        if (validationError is not null)
        {
            await ClassIslandProtocol.WriteAsync(
                pipe,
                new ClassContextResponse(false, null, validationError),
                cancellationToken);
            return;
        }

        var context = _contextService.QueryCurrentContext();
        await ClassIslandProtocol.WriteAsync(
            pipe,
            new ClassContextResponse(true, context),
            cancellationToken);
    }

    private async Task HandleDrawResultAsync(Stream pipe, RequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = envelope.Deserialize<DrawResultRequest>();
        var validationError = ClassIslandProtocol.Validate(request);
        if (validationError is not null)
        {
            await ClassIslandProtocol.WriteAsync(
                pipe,
                new ClassIslandResponse(false, validationError),
                cancellationToken);
            return;
        }

        if (_accessor.Provider is null)
        {
            await ClassIslandProtocol.WriteAsync(
                pipe,
                new ClassIslandResponse(false, "PickWeight 提醒提供方尚未就绪。"),
                cancellationToken);
            return;
        }

        _accessor.Provider.ShowDrawResult(request!.Names);
        await ClassIslandProtocol.WriteAsync(pipe, new ClassIslandResponse(true), cancellationToken);
    }
}
