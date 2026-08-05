using CommunityToolkit.Mvvm.ComponentModel;

namespace PickWeight.ClassIsland.Plugin;

public sealed class PickWeightNotificationSettings : ObservableRecipient
{
    private double _maskDurationSeconds = 5d;
    private double _overlayDurationSeconds = 10d;
    private bool _showOverlay = true;
    private bool _isMaskSpeechEnabled = true;
    private bool _isOverlaySpeechEnabled = true;

    public double MaskDurationSeconds
    {
        get => _maskDurationSeconds;
        set => SetProperty(ref _maskDurationSeconds, value);
    }

    public double OverlayDurationSeconds
    {
        get => _overlayDurationSeconds;
        set => SetProperty(ref _overlayDurationSeconds, value);
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set => SetProperty(ref _showOverlay, value);
    }

    public bool IsMaskSpeechEnabled
    {
        get => _isMaskSpeechEnabled;
        set => SetProperty(ref _isMaskSpeechEnabled, value);
    }

    public bool IsOverlaySpeechEnabled
    {
        get => _isOverlaySpeechEnabled;
        set => SetProperty(ref _isOverlaySpeechEnabled, value);
    }
}
