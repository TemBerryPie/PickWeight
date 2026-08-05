using PickWeight.Core.Abstractions;

namespace PickWeight_App.Services;

/// <summary>
/// The display-only contract between the quick-draw surface and the shared
/// draw execution flow.  Keeping the callbacks here means the window never
/// duplicates filtering, probability, persistence or notification rules.
/// </summary>
public sealed record QuickDrawPresentation(
    IReadOnlyList<string> CandidateNames,
    string FinalName,
    Func<CancellationToken, Task> CommitAsync,
    Func<CancellationToken, Task> CancelAsync);

public sealed class QuickDrawWindowService
{
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private QuickDrawWindow? _window;
    private QuickDrawResultWindow? _resultWindow;
    private CancellationTokenSource? _activeRequestCancellation;

    /// <summary>
    /// Set by application composition to prepare a one-person draw.  The handler
    /// returns <see langword="null"/> when there are no eligible candidates.
    /// </summary>
    public Func<CancellationToken, Task<QuickDrawPresentation?>>? QuickDrawRequested { get; set; }

    public QuickDrawWindowService(ISettingsService settings)
    {
        _settings = settings;
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
        App.DispatcherQueue.TryEnqueue(() =>
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

    /// <summary>
    /// Starts a quick draw if no other quick-draw request is active.  This is
    /// intentionally the sole command used by the floating shortcut and tray
    /// menu, so both paths share the same execution behavior.
    /// </summary>
    public async Task RequestQuickDrawAsync(CancellationToken cancellationToken = default)
        => await RequestDrawAsync(isPresentationMode: false, cancellationToken);

    /// <summary>Shows the shared quick-draw result on a large, projection-friendly surface.</summary>
    public async Task RequestPresentationDrawAsync(CancellationToken cancellationToken = default)
        => await RequestDrawAsync(isPresentationMode: true, cancellationToken);

    private async Task RequestDrawAsync(bool isPresentationMode, CancellationToken cancellationToken)
    {
        if (!await _requestGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var requestHandler = QuickDrawRequested;
            if (requestHandler is null)
            {
                return;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRequestCancellation = linkedCancellation;
            var presentation = await requestHandler(linkedCancellation.Token);
            if (presentation is null || string.IsNullOrWhiteSpace(presentation.FinalName))
            {
                return;
            }

            await ShowPresentationAsync(presentation, isPresentationMode, linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Closing the application or cancelling an input gesture simply
            // abandons the pending quick-draw presentation.
        }
        finally
        {
            _activeRequestCancellation = null;
            _requestGate.Release();
        }
    }

    public void Close()
    {
        _activeRequestCancellation?.Cancel();
        _window?.Close();
        _window = null;
        _resultWindow?.Close();
        _resultWindow = null;
    }

    private Task ShowPresentationAsync(QuickDrawPresentation presentation, bool isPresentationMode, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!App.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    _resultWindow?.Close();
                    var resultWindow = new QuickDrawResultWindow(isPresentationMode);
                    _resultWindow = resultWindow;
                    resultWindow.ApplyAppearance(_settings.Current);
                    await resultWindow.ShowAsync(
                        presentation.CandidateNames,
                        presentation.FinalName,
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
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("无法在 UI 线程显示快速抽取窗口。"));
        }

        return completion.Task;
    }
}
