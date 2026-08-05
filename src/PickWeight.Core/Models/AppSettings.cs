namespace PickWeight.Core.Models;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum AppBackgroundEffect
{
    Acrylic,
    Mica
}

public sealed class AppSettings
{
    public bool IsFirstRunCompleted { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public DrawMode DrawMode { get; set; } = DrawMode.Weighted;
    public int DrawTargetCount { get; set; } = 1;
    public string? DrawIncludeGroupName { get; set; }
    public string? DrawExcludeGroupName { get; set; }
    public int DrawExcludeRecentDrawCount { get; set; }
    public bool DrawOnlyNeverDrawn { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ShowQuickDrawButton { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Ctrl+Alt+P";
    public bool ShowDrawNotifications { get; set; } = true;
    public bool ShowClassIslandNotifications { get; set; } = true;
    public bool AutoResetWeights { get; set; }
    public int AutoResetDrawInterval { get; set; } = 20;
    public int DrawsSinceLastAutoReset { get; set; }
    public int FairnessReminderDays { get; set; } = 14;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public string LocalApiToken { get; set; } = string.Empty;
    public AppBackgroundEffect BackgroundEffect { get; set; } = AppBackgroundEffect.Acrylic;
    public double AcrylicBlurAmount { get; set; } = 0.45d;
    public double AnimationSpeed { get; set; } = 1d;
    public double AnimationMotionScale { get; set; } = 1d;
    public double AnimationBounceScale { get; set; } = 1d;

    public AppSettings Clone() => new()
    {
        IsFirstRunCompleted = IsFirstRunCompleted,
        Theme = Theme,
        DrawMode = DrawMode,
        DrawTargetCount = DrawTargetCount,
        DrawIncludeGroupName = DrawIncludeGroupName,
        DrawExcludeGroupName = DrawExcludeGroupName,
        DrawExcludeRecentDrawCount = DrawExcludeRecentDrawCount,
        DrawOnlyNeverDrawn = DrawOnlyNeverDrawn,
        StartWithWindows = StartWithWindows,
        ShowQuickDrawButton = ShowQuickDrawButton,
        GlobalHotkey = GlobalHotkey,
        ShowDrawNotifications = ShowDrawNotifications,
        ShowClassIslandNotifications = ShowClassIslandNotifications,
        AutoResetWeights = AutoResetWeights,
        AutoResetDrawInterval = AutoResetDrawInterval,
        DrawsSinceLastAutoReset = DrawsSinceLastAutoReset,
        FairnessReminderDays = FairnessReminderDays,
        WindowX = WindowX,
        WindowY = WindowY,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        LocalApiToken = LocalApiToken,
        BackgroundEffect = BackgroundEffect,
        AcrylicBlurAmount = AcrylicBlurAmount,
        AnimationSpeed = AnimationSpeed,
        AnimationMotionScale = AnimationMotionScale,
        AnimationBounceScale = AnimationBounceScale
    };

    public static AppSettings CreateDefaultPreservingRuntime(AppSettings current)
    {
        var defaults = new AppSettings
        {
            IsFirstRunCompleted = current.IsFirstRunCompleted,
            DrawsSinceLastAutoReset = current.DrawsSinceLastAutoReset
        };
        return defaults;
    }
}
