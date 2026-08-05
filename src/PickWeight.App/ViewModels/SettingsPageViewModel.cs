using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;

namespace PickWeight_App.ViewModels;

/// <summary>
/// Settings are persisted as the user changes them. Sliders use a brief debounce
/// so a drag does not write the state file for every individual pixel.
/// </summary>
public partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly DispatcherQueueTimer _saveTimer;
    private bool _isLoading;
    private bool _saveQueued;
    private Task? _activeSaveTask;

    [ObservableProperty] private int selectedThemeIndex;
    [ObservableProperty] private int selectedDrawModeIndex;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool showQuickDrawButton;
    [ObservableProperty] private string globalHotkey = string.Empty;
    [ObservableProperty] private bool showDrawNotifications;
    [ObservableProperty] private bool showClassIslandNotifications;
    [ObservableProperty] private bool autoResetWeights;
    [ObservableProperty] private double autoResetDrawInterval;
    [ObservableProperty] private double fairnessReminderDays;
    [ObservableProperty] private int selectedBackgroundEffectIndex;
    [ObservableProperty] private double acrylicBlurAmount;
    [ObservableProperty] private double animationSpeed;
    [ObservableProperty] private double animationMotionScale;
    [ObservableProperty] private double animationBounceScale;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool isSaving;

    public event Action? PreviewAnimationRequested;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsAcrylicSelected => SelectedBackgroundEffectIndex == (int)AppBackgroundEffect.Acrylic;
    public bool IsAutoResetIntervalEnabled => AutoResetWeights;
    public string AutoSaveStateText => IsSaving ? "正在自动保存…" : "所有更改都会自动保存。";

    public SettingsPageViewModel(ISettingsService settings)
    {
        _settings = settings;
        _saveTimer = App.DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            QueueSave();
        };
        LoadFromSettings(settings.Current);
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(AutoSaveStateText));
    partial void OnSelectedThemeIndexChanged(int value) => MarkChanged();
    partial void OnSelectedDrawModeIndexChanged(int value) => MarkChanged();
    partial void OnStartWithWindowsChanged(bool value) => MarkChanged();
    partial void OnShowQuickDrawButtonChanged(bool value) => MarkChanged();
    partial void OnGlobalHotkeyChanged(string value) => MarkChanged();
    partial void OnShowDrawNotificationsChanged(bool value) => MarkChanged();
    partial void OnShowClassIslandNotificationsChanged(bool value) => MarkChanged();
    partial void OnAutoResetWeightsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAutoResetIntervalEnabled));
        MarkChanged();
    }
    partial void OnAutoResetDrawIntervalChanged(double value) => MarkChanged(debounce: true);
    partial void OnFairnessReminderDaysChanged(double value) => MarkChanged(debounce: true);
    partial void OnSelectedBackgroundEffectIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAcrylicSelected));
        MarkChanged();
    }
    partial void OnAcrylicBlurAmountChanged(double value) => MarkChanged(debounce: true);
    partial void OnAnimationSpeedChanged(double value) => MarkChanged(debounce: true);
    partial void OnAnimationMotionScaleChanged(double value) => MarkChanged(debounce: true);
    partial void OnAnimationBounceScaleChanged(double value) => MarkChanged(debounce: true);

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        StatusMessage = string.Empty;
        StatusSeverity = InfoBarSeverity.Informational;
        LoadFromSettings(AppSettings.CreateDefaultPreservingRuntime(_settings.Current));
        await SaveAsync();
        if (StatusSeverity != InfoBarSeverity.Error)
        {
            StatusSeverity = InfoBarSeverity.Success;
            StatusMessage = "已恢复默认设置并自动保存。";
        }
    }

    [RelayCommand]
    private void PreviewAnimation()
    {
        PreviewAnimationRequested?.Invoke();
        StatusSeverity = InfoBarSeverity.Informational;
        StatusMessage = "已播放当前页面动画预览。";
    }

    public async Task SaveAsync()
    {
        _saveTimer.Stop();
        await QueueSave();
    }

    private void MarkChanged(bool debounce = false)
    {
        if (_isLoading)
        {
            return;
        }

        if (debounce)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
            return;
        }

        _saveTimer.Stop();
        _ = QueueSave();
    }

    private Task QueueSave()
    {
        _saveQueued = true;
        if (_activeSaveTask is null || _activeSaveTask.IsCompleted)
        {
            _activeSaveTask = SaveQueuedChangesAsync();
        }

        return _activeSaveTask;
    }

    private async Task SaveQueuedChangesAsync()
    {
        while (_saveQueued)
        {
            _saveQueued = false;
            IsSaving = true;
            try
            {
                await _settings.UpdateAsync(ApplyToSettings);
                if (StatusSeverity == InfoBarSeverity.Error)
                {
                    StatusMessage = string.Empty;
                    StatusSeverity = InfoBarSeverity.Informational;
                }
            }
            catch (Exception ex)
            {
                StatusSeverity = InfoBarSeverity.Error;
                StatusMessage = $"设置保存失败：{ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _isLoading = true;
        SelectedThemeIndex = (int)settings.Theme;
        SelectedDrawModeIndex = (int)settings.DrawMode;
        StartWithWindows = settings.StartWithWindows;
        ShowQuickDrawButton = settings.ShowQuickDrawButton;
        GlobalHotkey = settings.GlobalHotkey;
        ShowDrawNotifications = settings.ShowDrawNotifications;
        ShowClassIslandNotifications = settings.ShowClassIslandNotifications;
        AutoResetWeights = settings.AutoResetWeights;
        AutoResetDrawInterval = settings.AutoResetDrawInterval;
        FairnessReminderDays = settings.FairnessReminderDays;
        SelectedBackgroundEffectIndex = (int)settings.BackgroundEffect;
        AcrylicBlurAmount = settings.AcrylicBlurAmount;
        AnimationSpeed = settings.AnimationSpeed;
        AnimationMotionScale = settings.AnimationMotionScale;
        AnimationBounceScale = settings.AnimationBounceScale;
        _isLoading = false;
        OnPropertyChanged(nameof(IsAcrylicSelected));
        OnPropertyChanged(nameof(IsAutoResetIntervalEnabled));
    }

    private void ApplyToSettings(AppSettings settings)
    {
        settings.Theme = (AppTheme)Math.Clamp(SelectedThemeIndex, 0, 2);
        settings.DrawMode = (DrawMode)Math.Clamp(SelectedDrawModeIndex, 0, 3);
        settings.StartWithWindows = StartWithWindows;
        settings.ShowQuickDrawButton = ShowQuickDrawButton;
        settings.GlobalHotkey = GlobalHotkey.Trim();
        settings.ShowDrawNotifications = ShowDrawNotifications;
        settings.ShowClassIslandNotifications = ShowClassIslandNotifications;
        settings.AutoResetWeights = AutoResetWeights;
        settings.AutoResetDrawInterval = Math.Max(1, (int)Math.Round(AutoResetDrawInterval));
        settings.FairnessReminderDays = Math.Clamp((int)Math.Round(FairnessReminderDays), 1, 365);
        settings.BackgroundEffect = (AppBackgroundEffect)Math.Clamp(SelectedBackgroundEffectIndex, 0, 1);
        settings.AcrylicBlurAmount = Math.Clamp(AcrylicBlurAmount, 0d, 1d);
        settings.AnimationSpeed = Math.Clamp(AnimationSpeed, 0.5d, 2d);
        settings.AnimationMotionScale = Math.Clamp(AnimationMotionScale, 0d, 2d);
        settings.AnimationBounceScale = Math.Clamp(AnimationBounceScale, 0d, 2d);
    }
}
