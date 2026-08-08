using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages.Settings;

/// <summary>
/// 设置二级页共享导航逻辑：注入 SettingsPage 传入的共享 ViewModel，并处理返回动作。
/// SettingsPageViewModel 注册为 Transient，直接 DI 会每个页面拿到不同实例，
/// 因此统一由 SettingsPage 通过 Frame.Navigate 参数注入。
/// </summary>
internal static class SettingsSubPageNavigation
{
    /// <summary>在子页 OnNavigatedTo 中取出共享 ViewModel 并设为 DataContext。</summary>
    public static void ApplyViewModel(Page page, NavigationEventArgs e)
    {
        if (e.Parameter is SettingsPageViewModel vm)
        {
            page.DataContext = vm;
        }
    }

    /// <summary>
    /// 返回卡片列表：优先 Frame.GoBack()；当返回栈为空（首次导航到子页时通常如此）
    /// 时，由宿主 SettingsPage 直接切回列表视图。
    /// </summary>
    public static void GoBack(Page page)
    {
        if (page.Parent is Frame frame && frame.CanGoBack)
        {
            frame.GoBack();
            return;
        }

        FindHost(page)?.ReturnToCardList();
    }

    private static SettingsPage? FindHost(DependencyObject node)
    {
        var current = node;
        while (current is not null)
        {
            if (current is SettingsPage settings)
            {
                return settings;
            }

            current = (current as FrameworkElement)?.Parent as DependencyObject
                ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
