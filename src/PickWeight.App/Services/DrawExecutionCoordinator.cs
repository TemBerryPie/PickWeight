namespace PickWeight_App.Services;

/// <summary>
/// Serializes draw operations that share the mutable application state.  A
/// lease may span the quick-draw animation or the main-page result review, so
/// another entry point cannot prepare against state that is already pending.
/// </summary>
public interface IDrawExecutionCoordinator
{
    DrawExecutionLease? TryAcquire();

    Task<DrawExecutionLease> AcquireAsync(CancellationToken cancellationToken = default);
}

public sealed class DrawExecutionCoordinator : IDrawExecutionCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DrawExecutionLease? TryAcquire() =>
        _gate.Wait(0) ? new DrawExecutionLease(this) : null;

    public async Task<DrawExecutionLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DrawExecutionLease(this);
    }

    internal void Release() => _gate.Release();
}

public sealed class DrawExecutionLease : IDisposable
{
    private DrawExecutionCoordinator? _owner;

    internal DrawExecutionLease(DrawExecutionCoordinator owner) => _owner = owner;

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Release();
    }
}
