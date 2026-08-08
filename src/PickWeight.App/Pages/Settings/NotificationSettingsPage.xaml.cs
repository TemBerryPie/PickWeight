using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages.Settings;

/// <summary>通知设置二级页：抽取结果通知与 ClassIsland 提醒。</summary>
public sealed partial class NotificationSettingsPage : Page
{
    public NotificationSettingsPage()
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
