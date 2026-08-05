using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight_App.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly AppDataService _data;

    public SettingsService(AppDataService data)
    {
        _data = data;
    }

    public AppSettings Current => _data.State.Settings;
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public async Task UpdateAsync(Action<AppSettings> update)
    {
        var previous = Current.Clone();
        update(Current);

        await _data.SaveAsync();
        var args = new SettingsChangedEventArgs(previous, Current.Clone());
        if (args.HasChanges)
        {
            Changed?.Invoke(this, args);
        }
    }
}
