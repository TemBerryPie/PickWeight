using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages;

public sealed partial class DrawPage : Page
{
    private readonly AnimationService _animations;
    private readonly HashSet<UIElement> _animatedItems = [];
    private readonly DrawPageViewModel _viewModel;

    public DrawPage()
    {
        InitializeComponent();
        _animations = App.GetService<AnimationService>();
        _viewModel = App.GetService<DrawPageViewModel>();
        DataContext = _viewModel;
        _viewModel.DrawStarted += BeginResultAnimation;
        _viewModel.DrawCompleted += AnimateRealizedResults;
        Loaded += (_, _) => PlayPageEntrance();
    }

    private async void RulesButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BeginRuleEdit();
        RulesDialog.DataContext = _viewModel.RuleDraft;
        RulesDialog.XamlRoot = XamlRoot;
        await RulesDialog.ShowAsync();
    }

    private async void RulesDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await _viewModel.ApplyRuleDraftAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RulesDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        _viewModel.CancelRuleEdit();

    private void RulesDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _animations.PlayPopupEntrance(sender);
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e) => await _viewModel.ApplySelectedPresetAsync();

    private async void DeletePreset_Click(object sender, RoutedEventArgs e) => await _viewModel.DeleteSelectedPresetAsync();

    private async void PresentationDraw_Click(object sender, RoutedEventArgs e) =>
        await App.GetService<QuickDrawWindowService>().RequestPresentationDrawAsync();

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "预设名称", PlaceholderText = "例如：课堂提问" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "保存抽取预设",
            Content = nameBox,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await _viewModel.SavePresetAsync(nameBox.Text);
        }
        catch (ArgumentException exception)
        {
            await App.GetService<DialogService>().ShowAsync("无法保存预设", exception.Message);
        }
    }

    private void PlayPageEntrance()
    {
        _animations.PlayPageSection(HeaderSection, 0);
        _animations.PlayPageSection(ActionSection, 1);
        _animations.PlayPageSection(StatusSection, 2);
        _animations.PlayPageSection(ResultsList, 3);
    }

    private void ResultsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue &&
            args.ItemContainer is ListViewItem item &&
            item.ContentTemplateRoot is UIElement content)
        {
            AnimateResult(sender, item, content);
        }
    }

    private void BeginResultAnimation()
    {
        foreach (var item in _animatedItems)
        {
            _animations.Stop(item);
        }

        _animatedItems.Clear();
    }

    private void AnimateRealizedResults()
    {
        for (var index = 0; index < ResultsList.Items.Count; index++)
        {
            if (ResultsList.ContainerFromIndex(index) is ListViewItem item && item.ContentTemplateRoot is UIElement content)
            {
                AnimateResult(ResultsList, item, content);
            }
        }
    }

    private void AnimateResult(ListViewBase list, ListViewItem item, UIElement content)
    {
        if (_animatedItems.Add(content))
        {
            _animations.PlayResultReveal(content, Math.Max(list.IndexFromContainer(item), 0));
        }
    }
}
