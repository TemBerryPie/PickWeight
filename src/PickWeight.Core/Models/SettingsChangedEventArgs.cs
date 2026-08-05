namespace PickWeight.Core.Models;

public sealed class SettingsChangedEventArgs : EventArgs
{
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }

    public AppSettings Previous { get; }
    public AppSettings Current { get; }

    public bool HasChanges =>
        DrawModeChanged ||
        DrawRulesChanged ||
        ThemeChanged ||
        StartWithWindowsChanged ||
        ShowQuickDrawButtonChanged ||
        GlobalHotkeyChanged ||
        ShowDrawNotificationsChanged ||
        ShowClassIslandNotificationsChanged ||
        AutoResetChanged ||
        BackgroundChanged ||
        AnimationChanged;

    public bool DrawModeChanged => Previous.DrawMode != Current.DrawMode;
    public bool DrawRulesChanged =>
        DrawModeChanged ||
        Previous.DrawTargetCount != Current.DrawTargetCount ||
        !string.Equals(Previous.DrawIncludeGroupName, Current.DrawIncludeGroupName, StringComparison.Ordinal) ||
        !string.Equals(Previous.DrawExcludeGroupName, Current.DrawExcludeGroupName, StringComparison.Ordinal) ||
        Previous.DrawExcludeRecentDrawCount != Current.DrawExcludeRecentDrawCount ||
        Previous.DrawOnlyNeverDrawn != Current.DrawOnlyNeverDrawn;
    public bool ThemeChanged => Previous.Theme != Current.Theme;
    public bool StartWithWindowsChanged => Previous.StartWithWindows != Current.StartWithWindows;
    public bool ShowQuickDrawButtonChanged => Previous.ShowQuickDrawButton != Current.ShowQuickDrawButton;
    public bool GlobalHotkeyChanged => !string.Equals(Previous.GlobalHotkey, Current.GlobalHotkey, StringComparison.Ordinal);
    public bool ShowDrawNotificationsChanged => Previous.ShowDrawNotifications != Current.ShowDrawNotifications;
    public bool ShowClassIslandNotificationsChanged => Previous.ShowClassIslandNotifications != Current.ShowClassIslandNotifications;

    public bool AutoResetChanged =>
        Previous.AutoResetWeights != Current.AutoResetWeights ||
        Previous.AutoResetDrawInterval != Current.AutoResetDrawInterval ||
        Previous.DrawsSinceLastAutoReset != Current.DrawsSinceLastAutoReset;

    public bool BackgroundChanged =>
        Previous.BackgroundEffect != Current.BackgroundEffect ||
        Math.Abs(Previous.AcrylicBlurAmount - Current.AcrylicBlurAmount) > 0.000001d;

    public bool AnimationChanged =>
        Math.Abs(Previous.AnimationSpeed - Current.AnimationSpeed) > 0.000001d ||
        Math.Abs(Previous.AnimationMotionScale - Current.AnimationMotionScale) > 0.000001d ||
        Math.Abs(Previous.AnimationBounceScale - Current.AnimationBounceScale) > 0.000001d;

    public bool AffectsMainAppearance => ThemeChanged || BackgroundChanged;
    public bool AffectsQuickDrawWindow => ThemeChanged || ShowQuickDrawButtonChanged;
}
