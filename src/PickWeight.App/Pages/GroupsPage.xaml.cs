using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages;

public sealed partial class GroupsPage : Page
{
    public GroupsPage()
    {
        InitializeComponent();
        DataContext = App.GetService<GroupsPageViewModel>();
        Loaded += (_, _) => PlayPageEntrance();
    }

    private void PlayPageEntrance()
    {
        var animations = App.GetService<AnimationService>();
        animations.PlayPageSection(HeaderSection, 0);
        animations.PlayPageSection(CommandSection, 1);
        animations.PlayPageSection(EditorSection, 2);
    }

    private void GroupsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        PlayListItem(sender, args);

    private void GroupMembersList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        PlayListItem(sender, args);

    private static void PlayListItem(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue
            && args.ItemContainer is ListViewItem item
            && item.ContentTemplateRoot is UIElement content)
        {
            App.GetService<AnimationService>().PlayListItem(content, Math.Max(sender.IndexFromContainer(item), 0));
        }
    }

    private async void AddMembers_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GroupsPageViewModel viewModel || viewModel.SelectedGroup is null)
        {
            return;
        }

        viewModel.ResetAvailableMemberSearch();
        var searchBox = new TextBox
        {
            Header = "搜索成员",
            PlaceholderText = "输入姓名后显示可加入的成员"
        };
        var hint = new TextBlock
        {
            Text = "输入姓名关键字以搜索成员；未搜索时不会显示完整名单。",
            TextWrapping = TextWrapping.Wrap
        };
        var noResults = new TextBlock
        {
            Text = "没有匹配的可加入成员。",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var candidates = new ListView
        {
            ItemsSource = viewModel.FilteredAvailableMembers,
            SelectionMode = ListViewSelectionMode.Extended,
            MaxHeight = 360
        };
        void RefreshSearchState()
        {
            hint.Visibility = viewModel.HasAvailableMemberSearchQuery ? Visibility.Collapsed : Visibility.Visible;
            noResults.Visibility = viewModel.HasAvailableMemberSearchQuery && !viewModel.HasAvailableMembers
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        searchBox.TextChanged += (_, _) => viewModel.AvailableMemberSearchText = searchBox.Text;
        PropertyChangedEventHandler stateChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(GroupsPageViewModel.HasAvailableMemberSearchQuery)
                or nameof(GroupsPageViewModel.HasAvailableMembers))
            {
                RefreshSearchState();
            }
        };
        viewModel.PropertyChanged += stateChanged;
        RefreshSearchState();

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(searchBox);
        content.Children.Add(hint);
        content.Children.Add(noResults);
        content.Children.Add(candidates);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"添加成员到“{viewModel.SelectedGroup.GroupName}”",
            Content = content,
            PrimaryButtonText = "添加所选成员",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Opened += (_, _) => App.GetService<AnimationService>().PlayPopupEntrance(dialog);

        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.AddMembers(candidates.SelectedItems);
            }
        }
        finally
        {
            viewModel.PropertyChanged -= stateChanged;
            viewModel.ResetAvailableMemberSearch();
        }
    }

    private void RemoveMember_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GroupsPageViewModel viewModel && sender is FrameworkElement { Tag: string name })
        {
            viewModel.RemoveMember(name);
        }
    }
}
