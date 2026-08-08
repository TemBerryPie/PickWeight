using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PickWeight_App.ViewModels;

/// <summary>座位表页面的状态 VM（轻量，页面自己持有草稿）。</summary>
public sealed partial class SeatMapPageViewModel : ObservableObject, IEditablePageViewModel
{
    private Func<Task> _saveAsync = () => Task.CompletedTask;
    private Action _revert = () => { };

    public SeatMapPageViewModel()
    {
    }

    public void Initialize(Func<Task> saveAsync, Action revert)
    {
        _saveAsync = saveAsync;
        _revert = revert;
    }

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity statusSeverity = InfoBarSeverity.Success;
    [ObservableProperty] private bool hasUnassigned;
    [ObservableProperty] private bool isDirty;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public Visibility HasUnassignedVisibility => HasUnassigned ? Visibility.Visible : Visibility.Collapsed;

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnHasUnassignedChanged(bool value) => OnPropertyChanged(nameof(HasUnassignedVisibility));

    public void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    public Task SaveAsync() => _saveAsync();

    [RelayCommand]
    private Task SaveCommandAsync() => _saveAsync();

    [RelayCommand]
    private void Revert() => _revert();
}

