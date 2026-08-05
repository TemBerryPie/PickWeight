using System.Net;
using System.Text;
using System.Text.Json;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight_App.Services;

/// <summary>Token-protected, loopback-only control surface for local OBS and classroom integrations.</summary>
public sealed class LocalApiService : IDisposable
{
    private const string Prefix = "http://127.0.0.1:17861/";
    private readonly AppDataService _data;
    private readonly ISettingsService _settings;
    private readonly IDrawExecutionService _execution;
    private readonly AppLogService _log;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _stop;

    public LocalApiService(AppDataService data, ISettingsService settings, IDrawExecutionService execution, AppLogService log)
    {
        _data = data;
        _settings = settings;
        _execution = execution;
        _log = log;
    }

    public void Start()
    {
        if (_stop is not null) return;
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _stop = new CancellationTokenSource();
        _ = ListenAsync(_stop.Token);
    }

    public void Dispose()
    {
        _stop?.Cancel();
        _listener.Close();
        _stop?.Dispose();
        _stop = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleAsync(context, cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                await _log.InfoAsync($"Local API listener error: {exception.GetType().Name}.");
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, 401, new { error = "Unauthorized" }, cancellationToken);
                return;
            }

            switch (context.Request.HttpMethod, context.Request.Url?.AbsolutePath)
            {
                case ("GET", "/v1/health"):
                    await WriteJsonAsync(context.Response, 200, new { status = "ok", version = typeof(App).Assembly.GetName().Version?.ToString(3) }, cancellationToken);
                    break;
                case ("GET", "/v1/recent"):
                    var latest = _data.State.DrawHistory.OrderByDescending(item => item.DrawnAt).FirstOrDefault();
                    await WriteJsonAsync(context.Response, 200, new { drawnAt = latest?.DrawnAt, names = latest?.MemberNames ?? [] }, cancellationToken);
                    break;
                case ("POST", "/v1/draw"):
                    var names = await DrawOnUiThreadAsync(cancellationToken);
                    await WriteJsonAsync(context.Response, 200, new { names }, cancellationToken);
                    break;
                default:
                    await WriteJsonAsync(context.Response, 404, new { error = "NotFound" }, cancellationToken);
                    break;
            }
        }
        catch (Exception exception)
        {
            await _log.InfoAsync($"Local API request failed: {exception.GetType().Name}.");
            await WriteJsonAsync(context.Response, 500, new { error = "RequestFailed" }, cancellationToken);
        }
    }

    private bool IsAuthorized(HttpListenerRequest request) =>
        string.Equals(request.Headers["X-PickWeight-Token"], _settings.Current.LocalApiToken, StringComparison.Ordinal);

    private Task<IReadOnlyList<string>> DrawOnUiThreadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!App.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var settings = _settings.Current;
                var prepared = await _execution.PrepareAsync(new DrawExecutionRequest(
                    Math.Max(1, settings.DrawTargetCount),
                    new DrawFilterOptions(settings.DrawIncludeGroupName, settings.DrawExcludeGroupName,
                        Math.Max(0, settings.DrawExcludeRecentDrawCount), settings.DrawOnlyNeverDrawn),
                    settings.DrawMode), cancellationToken);
                var outcome = await _execution.CommitAsync(prepared, prepared.SelectedMembers, confirmSelection: true, cancellationToken: cancellationToken);
                completion.TrySetResult(outcome.CommittedNames);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }))
        {
            completion.TrySetException(new InvalidOperationException("应用 UI 线程不可用。"));
        }
        return completion.Task;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object value, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }
}
