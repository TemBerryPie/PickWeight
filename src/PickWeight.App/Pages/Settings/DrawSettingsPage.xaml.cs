using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages.Settings;

/// <summary>点名设置二级页：默认模式、自动重置与公平性提醒天数。</summary>
public sealed partial class DrawSettingsPage : Page
{
    public DrawSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayEntrance();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SettingsSubPageNavigation.ApplyViewModel(this, e);
    }

    private void PlayEntrance()
    {
        var animations = App.GetService<AnimationService>();
        animations.PlayPageSection(HeaderPanel, 0);
        animations.PlayPageSection(ContentPanel, 1);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        SettingsSubPageNavigation.GoBack(this);
}
