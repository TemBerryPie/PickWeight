using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsPageViewModel _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = App.GetService<SettingsPageViewModel>();
        DataContext = _viewModel;
        _viewModel.PreviewAnimationRequested += PreviewAnimation;
        Unloaded += SettingsPage_Unloaded;
        Loaded += (_, _) => PlayPageEntrance();
    }

    private void PlayPageEntrance()
    {
        var animations = App.GetService<AnimationService>();
        animations.PlayPageSection(HeaderSection, 0);
        animations.PlayPageSection(StatusSection, 1);
        var cards = new UIElement[]
        {
            SystemCategoryCard,
            DrawCategoryCard,
            NotificationCategoryCard,
            AppearanceCategoryCard
        };
        for (var index = 0; index < cards.Length; index++)
        {
            animations.PlayListItem(cards[index], index + 1);
        }
    }

    private async void SystemCategoryCard_Click(object sender, RoutedEventArgs e) =>
        await ShowSettingsDialogAsync(SystemSettingsDialog);

    private async void DrawCategoryCard_Click(object sender, RoutedEventArgs e) =>
        await ShowSettingsDialogAsync(DrawSettingsDialog);

    private async void NotificationCategoryCard_Click(object sender, RoutedEventArgs e) =>
        await ShowSettingsDialogAsync(NotificationSettingsDialog);

    private async void AppearanceCategoryCard_Click(object sender, RoutedEventArgs e) =>
        await ShowSettingsDialogAsync(AppearanceSettingsDialog);

    private async void FirstRunWizard_Click(object sender, RoutedEventArgs e)
    {
        SystemSettingsDialog.Hide();
        await App.GetService<FirstRunWizardService>().ShowAsync(XamlRoot, markCompleted: false);
    }

    private async void ShowLocalApiToken_Click(object sender, RoutedEventArgs e)
    {
        var tokenBox = new TextBox
        {
            Header = "X-PickWeight-Token",
            IsReadOnly = true,
            Text = App.GetService<PickWeight.Core.Abstractions.ISettingsService>().Current.LocalApiToken
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "仅供本机 OBS 或自动化脚本使用，请勿公开。", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(tokenBox);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "本地 API 令牌", Content = panel, CloseButtonText = "关闭" };
        await dialog.ShowAsync();
    }

    private async Task ShowSettingsDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
    }

    private void SettingsDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        App.GetService<AnimationService>().PlayPopupEntrance(sender);
    }

    private void PreviewAnimation() => PlayPageEntrance();

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.PreviewAnimationRequested -= PreviewAnimation;
        Unloaded -= SettingsPage_Unloaded;
    }
}
