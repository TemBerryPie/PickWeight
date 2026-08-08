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

    /// <summary>抽取历史保留天数，超期条目会自动归档到班级文件夹的 archive 目录。</summary>
    public int HistoryRetentionDays { get; set; } = 180;

    /// <summary>统计页是否包含已归档的历史数据一起展示。</summary>
    public bool HistoryIncludeArchived { get; set; }

    /// <summary>开启后本轮已抽到的成员不再参与抽取，直到候选池抽空自动进入下一轮。</summary>
    public bool DrawNoRepeatInRound { get; set; } = true;

    /// <summary>座位区域过滤（全部/指定列/指定排/靠门/靠窗）。</summary>
    public SeatRegion DrawSeatRegion { get; set; } = SeatRegion.All;

    /// <summary>指定列号（1 起，靠门侧为 1）。</summary>
    public int DrawSeatColumn { get; set; } = 1;

    /// <summary>指定排号（1 起，每列独立计数）。</summary>
    public int DrawSeatRow { get; set; } = 1;

    /// <summary>课堂表现评价是否影响成员权重（良好/一般会降低权重）。</summary>
    public bool RatingAffectsWeight { get; set; }

    /// <summary>评价「良好」时的权重倍率。</summary>
    public double RatingWeightMultiplier { get; set; } = 0.7d;

    /// <summary>评价「一般」时的权重倍率。</summary>
    public double RatingFairMultiplier { get; set; } = 0.9d;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }

    /// <summary>主界面左侧导航窗格是否常驻展开（折叠时仅显示 56px 图标栏）。</summary>
    public bool IsNavigationPaneOpen { get; set; } = true;
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
        HistoryRetentionDays = HistoryRetentionDays,
        HistoryIncludeArchived = HistoryIncludeArchived,
        DrawNoRepeatInRound = DrawNoRepeatInRound,
        DrawSeatRegion = DrawSeatRegion,
        DrawSeatColumn = DrawSeatColumn,
        DrawSeatRow = DrawSeatRow,
        RatingAffectsWeight = RatingAffectsWeight,
        RatingWeightMultiplier = RatingWeightMultiplier,
        RatingFairMultiplier = RatingFairMultiplier,
        WindowX = WindowX,
        WindowY = WindowY,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        IsNavigationPaneOpen = IsNavigationPaneOpen,
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
            DrawsSinceLastAutoReset = current.DrawsSinceLastAutoReset,
            IsNavigationPaneOpen = current.IsNavigationPaneOpen
        };
        return defaults;
    }
}
