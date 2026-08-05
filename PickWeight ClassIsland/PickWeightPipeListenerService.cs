using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using PickWeight.ClassIsland.Protocol;

namespace PickWeight.ClassIsland.Plugin;

public sealed class PickWeightPipeListenerService : BackgroundService
{
    private readonly NotificationProviderAccessor _accessor;

    public PickWeightPipeListenerService(NotificationProviderAccessor accessor)
    {
        _accessor = accessor;
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
        ClassIslandResponse response;
        try
        {
            var request = await ClassIslandProtocol.ReadAsync<DrawResultRequest>(pipe, cancellationToken);
            var validationError = ClassIslandProtocol.Validate(request);
            if (validationError is not null)
            {
                response = new ClassIslandResponse(false, validationError);
            }
            else if (_accessor.Provider is null)
            {
                response = new ClassIslandResponse(false, "PickWeight 提醒提供方尚未就绪。");
            }
            else
            {
                _accessor.Provider.ShowDrawResult(request!.Names);
                response = new ClassIslandResponse(true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            response = new ClassIslandResponse(false, "请求格式无效。");
        }

        await ClassIslandProtocol.WriteAsync(pipe, response, cancellationToken);
    }
}
