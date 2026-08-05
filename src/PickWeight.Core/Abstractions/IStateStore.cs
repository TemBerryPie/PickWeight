using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface IStateStore
{
    Task<AppState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppState state, CancellationToken cancellationToken = default);
}
