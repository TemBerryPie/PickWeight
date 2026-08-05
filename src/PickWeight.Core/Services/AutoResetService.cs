using PickWeight.Core.Models;

namespace PickWeight.Core.Services;

public static class AutoResetService
{
    public static bool RecordSuccessfulDraw(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var settings = state.Settings;
        if (!settings.AutoResetWeights)
        {
            return false;
        }

        var interval = Math.Max(1, settings.AutoResetDrawInterval);
        settings.AutoResetDrawInterval = interval;
        settings.DrawsSinceLastAutoReset = Math.Max(0, settings.DrawsSinceLastAutoReset) + 1;
        if (settings.DrawsSinceLastAutoReset < interval)
        {
            return false;
        }

        foreach (var member in state.Members)
        {
            MemberRules.Reset(member);
        }

        settings.DrawsSinceLastAutoReset = 0;
        return true;
    }
}
