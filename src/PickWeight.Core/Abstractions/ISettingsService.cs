using PickWeight.Core.Models;

namespace PickWeight.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler<SettingsChangedEventArgs>? Changed;
    Task UpdateAsync(Action<AppSettings> update);
}
