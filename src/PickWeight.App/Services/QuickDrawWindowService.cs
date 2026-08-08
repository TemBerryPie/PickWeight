using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

/// <summary>
/// The display-only contract between the quick-draw surface and the shared
/// draw execution flow.  Keeping the callbacks here means the window never
/// duplicates filtering, probability, persistence or notification rules.
/// </summary>
public sealed record QuickDrawPresentation(
    IReadOnlyList<string> CandidateNames,
    IReadOnlyList<string> FinalNames,
    IReadOnlyList<string> RollingNames,
    Func<CancellationToken, Task> CommitAsync,
    Func<CancellationToken, Task> CancelAsync)
{
    /// <summary>首个名字，兼容单名调用方。</summary>
    public string FinalName => FinalNames.Count > 0 ? FinalNames[0] : string.Empty;
}

public enum QuickDrawRequestStatus
{
    Accepted,
    Busy,
    NoCandidates,
    Unavailable,
    Canceled
}

public sealed record QuickDrawRequestResult(QuickDrawRequestStatus Status)
{
    public bool IsAccepted => Status == QuickDrawRequestStatus.Accepted;
}

public sealed class QuickDrawWindowService
{
    private readonly ISettingsService _settings;
    private readonly IDrawExecutionCoordinator _drawCoordinator;
    private readonly AppLogService _log;
    private QuickDrawWindow? _window;
    private QuickDrawResultWindow? _resultWindow;
    private CancellationTokenSource? _activeRequestCancellation;

    /// <summary>
    /// Set by application composition to prepare a one-person draw.  The handler
    /// returns <see langword="null"/> when there are no eligible candidates.
    /// </summary>
    public Func<CancellationToken, Task<QuickDrawPresentation?>>? QuickDrawRequested { get; set; }

    internal IDrawExecutionCoordinator ExecutionCoordinator => _drawCoordinator;

    public QuickDrawWindowService(ISettingsService settings)
        : this(settings, new DrawExecutionCoordinator(), new AppLogService())
    {
    }

    public QuickDrawWindowService(
        ISettingsService settings,
        IDrawExecutionCoordinator drawCoordinator,
        AppLogService log)
    {
        _settings = settings;
        _drawCoordinator = drawCoordinator;
        _log = log;
        _settings.Changed += (_, args) =>
        {
            if (args.AffectsQuickDrawWindow)
            {
                Apply();
            }
        };
    }

    public void Apply()
    {
        var enqueued = false;
        try
        {
            enqueued = App.DispatcherQueue is not null && App.DispatcherQueue.TryEnqueue(() =>
            {
                _resultWindow?.ApplyAppearance(_settings.Current);
                if (!_settings.Current.ShowQuickDrawButton)
                {
                    _window?.Close();
                    _window = null;
                    return;
                }

                _window ??= new QuickDrawWindow();
                _window.ApplyAppearance(_settings.Current);
                _window.AppWindow.Show(false);
            });
        }
        catch
        {
            // A startup/shutdown race can make the dispatcher throw instead
            // of returning false; both cases are handled as unavailable UI.
        }

        if (!enqueued)
        {
            _ = TryLogAsync("Quick-draw surface unavailable: UI dispatcher is not ready.");
        }
    }

    /// <summary>
    /// Starts a quick draw if no other quick-draw request is active.  This is
    /// intentionally the sole command used by the floating shortcut and tray
    /// menu, so both paths share the same execution behavior.
    /// </summary>
    public async Task RequestQuickDrawAsync(CancellationToken cancellationToken = default)
        => await RequestDrawAsync(cancellationToken, waitForPresentation: true);

    /// <summary>
    /// Starts the compact quick-draw presentation and returns as soon as the
    /// result window has been queued.  The shared execution lease remains held
    /// until the window closes, so callers can report a deterministic 409 while
    /// a result is still visible instead of starting a second draw.
    /// </summary>
    public Task<QuickDrawRequestResult> TryStartQuickDrawAsync(CancellationToken cancellationToken = default)
        => RequestDrawAsync(cancellationToken, waitForPresentation: false);

    private async Task<QuickDrawRequestResult> RequestDrawAsync(
        CancellationToken cancellationToken,
        bool waitForPresentation)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new QuickDrawRequestResult(QuickDrawRequestStatus.Canceled);
        }

        CancellationTokenSource? linkedCancellation = null;
        DrawExecutionLease? drawLease = _drawCoordinator.TryAcquire();
        if (drawLease is null)
        {
            return new QuickDrawRequestResult(QuickDrawRequestStatus.Busy);
        }

        QuickDrawPresentation? presentation = null;
        var handedOff = false;
        try
        {
            var requestHandler = QuickDrawRequested;
            if (requestHandler is null)
            {
                return new QuickDrawRequestResult(QuickDrawRequestStatus.Unavailable);
            }

            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRequestCancellation = linkedCancellation;
            presentation = await requestHandler(linkedCancellation.Token);
            if (presentation is null || string.IsNullOrWhiteSpace(presentation.FinalName))
            {
                return new QuickDrawRequestResult(QuickDrawRequestStatus.NoCandidates);
            }

            presentation = WrapCancellation(presentation);
            if (waitForPresentation)
            {
                var presentationShown = false;
                try
                {
                    await ShowPresentationAsync(
                        presentation,
                        linkedCancellation.Token,
                        () => presentationShown = true);
                }
                catch (OperationCanceledException)
                {
                    if (!presentationShown)
                    {
                        await DiscardAfterPresentationFailureAsync(presentation, "cancelled");
                    }

                    return new QuickDrawRequestResult(QuickDrawRequestStatus.Canceled);
                }
                catch (Exception exception)
                {
                    if (!presentationShown)
                    {
                        await DiscardAfterPresentationFailureAsync(
                            presentation,
                            $"display failed ({exception.GetType().Name})");
                    }

                    throw;
                }
            }
            else
            {
                _ = FinishBackgroundRequestAsync(presentation, linkedCancellation, drawLease);
                handedOff = true;
                drawLease = null;
            }

            return new QuickDrawRequestResult(QuickDrawRequestStatus.Accepted);
        }
        catch (OperationCanceledException)
        {
            // Closing the application or cancelling an input gesture simply
            // abandons the pending quick-draw presentation.
            return new QuickDrawRequestResult(QuickDrawRequestStatus.Canceled);
        }
        finally
        {
            if (!handedOff)
            {
                ReleaseRequest(linkedCancellation, drawLease);
            }
        }
    }

    private async Task FinishBackgroundRequestAsync(
        QuickDrawPresentation presentation,
        CancellationTokenSource linkedCancellation,
        DrawExecutionLease drawLease)
    {
        try
        {
            var presentationShown = false;
            try
            {
                await ShowPresentationAsync(
                    presentation,
                    linkedCancellation.Token,
                    () => presentationShown = true);
            }
            catch (OperationCanceledException)
            {
                if (!presentationShown)
                {
                    await DiscardAfterPresentationFailureAsync(presentation, "cancelled");
                }
                else
                {
                    await TryLogAsync("Quick draw display cancelled after the result window was shown.");
                }
            }
            catch (Exception exception)
            {
                if (!presentationShown)
                {
                    await DiscardAfterPresentationFailureAsync(
                        presentation,
                        $"display failed ({exception.GetType().Name})");
                }
                else
                {
                    await TryLogAsync($"Quick draw display failed after the result window was shown ({exception.GetType().Name}).");
                    CloseResultWindowOnUiThread();
                }
            }
        }
        finally
        {
            ReleaseRequest(linkedCancellation, drawLease);
        }
    }

    private async Task DiscardAfterPresentationFailureAsync(
        QuickDrawPresentation presentation,
        string reason)
    {
        try
        {
            await presentation.CancelAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await TryLogAsync($"Quick draw {reason}; discard failed ({exception.GetType().Name}).");
            return;
        }

        await TryLogAsync($"Quick draw {reason}; prepared draw discarded.");
    }

    private async Task TryLogAsync(string message)
    {
        try
        {
            await _log.InfoAsync(message, CancellationToken.None);
        }
        catch
        {
            // Diagnostics must not keep the request gate held.
        }
    }

    private void ReleaseRequest(
        CancellationTokenSource? linkedCancellation,
        DrawExecutionLease? drawLease)
    {
        if (linkedCancellation is not null
            && ReferenceEquals(_activeRequestCancellation, linkedCancellation))
        {
            _activeRequestCancellation = null;
        }

        linkedCancellation?.Dispose();
        drawLease?.Dispose();
    }

    public void Close()
    {
        _activeRequestCancellation?.Cancel();
        _window?.Close();
        _window = null;
        _resultWindow?.Close();
        _resultWindow = null;
    }

    private Task ShowPresentationAsync(
        QuickDrawPresentation presentation,
        CancellationToken cancellationToken,
        Action presentationShown)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
            return completion.Task;
        }

        var callbackState = 0;
        var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (Interlocked.CompareExchange(ref callbackState, 2, 0) == 0)
            {
                _ = CancelBeforePresentationAsync(presentation, completion, cancellationToken);
            }
        });
        _ = completion.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        var enqueued = false;
        try
        {
            enqueued = App.DispatcherQueue is not null && App.DispatcherQueue.TryEnqueue(async () =>
            {
                if (Interlocked.CompareExchange(ref callbackState, 1, 0) != 0)
                {
                    return;
                }

                QuickDrawResultWindow? resultWindow = null;
                try
                {
                    _resultWindow?.Close();
                    resultWindow = new QuickDrawResultWindow();
                    _resultWindow = resultWindow;
                    presentationShown();
                    resultWindow.ApplyAppearance(_settings.Current);
                    await resultWindow.ShowAsync(
                        presentation.CandidateNames,
                        presentation.RollingNames,
                        presentation.FinalNames,
                        presentation.CommitAsync,
                        presentation.CancelAsync,
                        cancellationToken);
                    if (ReferenceEquals(_resultWindow, resultWindow))
                    {
                        _resultWindow = null;
                    }

                    completion.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    if (resultWindow is not null && !resultWindow.IsAnimationCompleted)
                    {
                        resultWindow.Close();
                        if (!resultWindow.HasCancelCallback)
                        {
                            await TryDiscardPresentationAsync(presentation);
                        }
                    }

                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    if (resultWindow is not null && !resultWindow.IsAnimationCompleted)
                    {
                        resultWindow.Close();
                        if (!resultWindow.HasCancelCallback)
                        {
                            await TryDiscardPresentationAsync(presentation);
                        }
                    }

                    completion.TrySetException(exception);
                }
            });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        if (!enqueued && !completion.Task.IsCompleted)
        {
            completion.TrySetException(new InvalidOperationException("无法在 UI 线程显示快速抽取窗口。"));
        }

        return completion.Task;
    }

    private async Task CancelBeforePresentationAsync(
        QuickDrawPresentation presentation,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        await TryDiscardPresentationAsync(presentation);
        completion.TrySetCanceled(cancellationToken);
    }

    private static QuickDrawPresentation WrapCancellation(QuickDrawPresentation presentation)
    {
        var cancelStarted = 0;
        var cancelAsync = presentation.CancelAsync;
        return presentation with
        {
            CancelAsync = cancellationToken =>
                Interlocked.Exchange(ref cancelStarted, 1) == 0
                    ? cancelAsync(cancellationToken)
                    : Task.CompletedTask
        };
    }

    private async Task TryDiscardPresentationAsync(QuickDrawPresentation presentation)
    {
        try
        {
            await presentation.CancelAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await TryLogAsync($"Quick draw display cleanup failed ({exception.GetType().Name}).");
        }
    }

    private void CloseResultWindowOnUiThread()
    {
        var enqueued = false;
        try
        {
            enqueued = App.DispatcherQueue is not null && App.DispatcherQueue.TryEnqueue(() => _resultWindow?.Close());
        }
        catch
        {
            // Fall through to the diagnostic below.
        }

        if (!enqueued)
        {
            _ = TryLogAsync("Quick draw result window could not be closed after a display failure.");
        }
    }
}
