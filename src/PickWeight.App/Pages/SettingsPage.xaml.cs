using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PickWeight_App.Pages.Settings;
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
            RatingCategoryCard,
            HistoryArchiveCategoryCard,
            NotificationCategoryCard,
            AppearanceCategoryCard
        };
        for (var index = 0; index < cards.Length; index++)
        {
            animations.PlayListItem(cards[index], index + 1);
        }
    }

    private void SystemCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(SystemSettingsPage));

    private void DrawCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(DrawSettingsPage));

    private void RatingCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(RatingSettingsPage));

    private void HistoryArchiveCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(HistoryArchiveSettingsPage));

    private void NotificationCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(NotificationSettingsPage));

    private void AppearanceCategoryCard_Click(object sender, RoutedEventArgs e) =>
        OpenSettingsPage(typeof(AppearanceSettingsPage));

    /// <summary>
    /// 在 SettingsFrame 中打开二级设置页，并把本页持有的 SettingsPageViewModel
    /// 作为导航参数传入，保证所有子页共享同一实例（Transient 直接 DI 会拿到不同实例）。
    /// </summary>
    private void OpenSettingsPage(Type pageType)
    {
        // 清空当前内容，避免 Frame.Navigate 把上一个子页压入 BackStack，
        // 导致从子页返回时回到上一个子页而非卡片列表。
        SettingsFrame.Content = null;
        SettingsFrame.BackStack.Clear();
        SettingsFrame.ForwardStack.Clear();
        SettingsFrame.Navigate(pageType, _viewModel);
        // 同类型复用导航可能不触发 Navigated，这里显式切换视图
        SettingsListScrollViewer.Visibility = Visibility.Collapsed;
        SettingsFrame.Visibility = Visibility.Visible;
    }

    private void SettingsFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var showingChild = SettingsFrame.Content is not null;
        SettingsListScrollViewer.Visibility = showingChild ? Visibility.Collapsed : Visibility.Visible;
        SettingsFrame.Visibility = showingChild ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>子页返回时切回卡片列表视图。</summary>
    internal void ReturnToCardList()
    {
        SettingsFrame.BackStack.Clear();
        SettingsFrame.ForwardStack.Clear();
        SettingsListScrollViewer.Visibility = Visibility.Visible;
        SettingsFrame.Visibility = Visibility.Collapsed;
    }
}
