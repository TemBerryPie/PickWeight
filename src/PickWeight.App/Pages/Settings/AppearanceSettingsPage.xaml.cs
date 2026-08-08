using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages.Settings;

/// <summary>外观和个性化设置二级页：主题、背景、动画；支持预览动画。</summary>
public sealed partial class AppearanceSettingsPage : Page
{
    private SettingsPageViewModel? _viewModel;

    public AppearanceSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayEntrance();
        Unloaded += AppearanceSettingsPage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SettingsSubPageNavigation.ApplyViewModel(this, e);
        _viewModel = DataContext as SettingsPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PreviewAnimationRequested -= PreviewAnimation;
            _viewModel.PreviewAnimationRequested += PreviewAnimation;
        }
    }

    private void PreviewAnimation() => PlayEntrance();

    private void PlayEntrance()
    {
        var animations = App.GetService<AnimationService>();
        animations.PlayPageSection(HeaderPanel, 0);
        animations.PlayPageSection(ContentPanel, 1);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        SettingsSubPageNavigation.GoBack(this);

    private void AppearanceSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PreviewAnimationRequested -= PreviewAnimation;
        }

        Unloaded -= AppearanceSettingsPage_Unloaded;
    }
}
