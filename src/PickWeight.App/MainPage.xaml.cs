using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight_App.Pages;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App;

/// <summary>
/// 主壳层只负责导航和页面离开确认，业务状态全部保留在 ViewModel 与服务中。
/// </summary>
public sealed partial class MainPage : Page
{
    private bool _restoringSelection;
    private NavigationViewItem? _currentItem;

    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _currentItem = (NavigationViewItem)Navigation.MenuItems[0];
            Navigation.SelectedItem = _currentItem;
            ContentFrame.Navigate(typeof(DrawPage));
        };
    }

    public async Task NavigateToDrawAndDrawAsync()
    {
        if (!await ConfirmPageExitAsync())
        {
            return;
        }

        var drawItem = (NavigationViewItem)Navigation.MenuItems[0];
        _restoringSelection = true;
        Navigation.SelectedItem = drawItem;
        _restoringSelection = false;
        _currentItem = drawItem;

        if (ContentFrame.Content is not DrawPage)
        {
            ContentFrame.Navigate(typeof(DrawPage));
        }

        if (ContentFrame.Content is DrawPage drawPage
            && drawPage.DataContext is DrawPageViewModel viewModel)
        {
            await viewModel.DrawCommand.ExecuteAsync(null);
        }
    }

    private async Task<bool> ConfirmPageExitAsync()
    {
        if (ContentFrame.Content is not FrameworkElement element
            || element.DataContext is not IEditablePageViewModel editable
            || !editable.IsDirty)
        {
            return true;
        }

        var choice = await App.GetService<DialogService>().ConfirmUnsavedAsync();
        if (choice == UnsavedChoice.Cancel)
        {
            return false;
        }

        if (choice == UnsavedChoice.Save)
        {
            await editable.SaveAsync();
        }

        return true;
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_restoringSelection)
        {
            return;
        }
        NavigationViewItem? selected = args.IsSettingsSelected ? null : args.SelectedItemContainer as NavigationViewItem;
        if (!args.IsSettingsSelected && (selected is null || selected == _currentItem)) return;

        if (!await ConfirmPageExitAsync())
        {
            _restoringSelection = true;
            Navigation.SelectedItem = _currentItem;
            _restoringSelection = false;
            return;
        }

        _currentItem = selected;
        ContentFrame.Navigate(args.IsSettingsSelected ? typeof(SettingsPage) : selected?.Tag?.ToString() switch
        {
            "history" => typeof(HistoryPage),
            "members" => typeof(MembersPage),
            "groups" => typeof(GroupsPage),
            _ => typeof(DrawPage)
        });
    }
}
