using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages;

public sealed partial class MembersPage : Page
{
    public MembersPage()
    {
        InitializeComponent();
        DataContext = App.GetService<MembersPageViewModel>();
        Loaded += async (_, _) =>
        {
            if (DataContext is MembersPageViewModel viewModel)
            {
                await viewModel.EnsureLoadedAsync();
            }

            PlayPageEntrance();
        };
    }

    private void PlayPageEntrance()
    {
        var animations = App.GetService<AnimationService>();
        var sections = new UIElement[] { HeaderSection, CommandSection, SearchSection, ListSection };
        for (var index = 0; index < sections.Length; index++)
        {
            animations.PlayPageSection(sections[index], index);
        }
    }

    private void MembersList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue
            && args.ItemContainer is ListViewItem item
            && item.ContentTemplateRoot is UIElement content)
        {
            App.GetService<AnimationService>().PlayListItem(content, Math.Max(sender.IndexFromContainer(item), 0));
        }
    }

    private async void AddMember_Click(object sender, RoutedEventArgs e) => await ShowMemberEditorAsync(null);

    private async void EditMember_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MemberRowViewModel member })
        {
            await ShowMemberEditorAsync(member);
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MembersPageViewModel viewModel)
        {
            await viewModel.DeleteRowsAsync(MembersList.SelectedItems);
        }
    }

    private async void EnableSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MembersPageViewModel viewModel)
        {
            await viewModel.SetRowsActiveAsync(MembersList.SelectedItems, isActive: true);
        }
    }

    private async void DisableSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MembersPageViewModel viewModel)
        {
            await viewModel.SetRowsActiveAsync(MembersList.SelectedItems, isActive: false);
        }
    }

    private async void SetWeightSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MembersPageViewModel viewModel) return;
        var weightBox = new NumberBox { Header = "统一显式权重", Minimum = 0, Maximum = MemberRules.MaximumWeight, Value = MemberRules.DefaultExplicitWeight };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "批量设置权重",
            Content = weightBox,
            PrimaryButtonText = "应用",
            CloseButtonText = "取消"
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await viewModel.SetRowsWeightAsync(MembersList.SelectedItems, weightBox.Value);
        }
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MembersPageViewModel viewModel) await viewModel.UndoAsync();
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MembersPageViewModel viewModel) await viewModel.RedoAsync();
    }

    private async Task ShowMemberEditorAsync(MemberRowViewModel? member)
    {
        if (DataContext is not MembersPageViewModel viewModel)
        {
            return;
        }

        var nameBox = new TextBox
        {
            Header = "姓名",
            Text = member?.Name ?? string.Empty,
            PlaceholderText = "输入成员姓名"
        };
        var genderOptions = new[]
        {
            new GenderOption(null, "未设置"),
            new GenderOption(MemberGender.Male, "男"),
            new GenderOption(MemberGender.Female, "女")
        };
        var genderBox = new ComboBox
        {
            Header = "性别",
            ItemsSource = genderOptions,
            DisplayMemberPath = nameof(GenderOption.Label),
            SelectedIndex = member?.Gender switch
            {
                MemberGender.Male => 1,
                MemberGender.Female => 2,
                _ => 0
            }
        };
        var weightBox = new NumberBox
        {
            Header = "显式权重",
            Minimum = 0,
            Maximum = 1_000_000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = member?.Weight ?? MemberRules.DefaultExplicitWeight
        };
        var calledCountBox = new NumberBox
        {
            Header = "被点次数",
            Minimum = 0,
            Maximum = int.MaxValue,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = member?.CalledCount ?? 0
        };
        var validationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var readOnlyDetails = new StackPanel { Spacing = 4 };
        readOnlyDetails.Children.Add(new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "自动计算信息"
        });
        readOnlyDetails.Children.Add(new TextBlock
        {
            Text = member is null
                ? "智能权重与被点权重会在保存后根据被点次数自动计算。"
                : $"智能权重：{member.SmartWeightText}；被点权重：{member.CalledWeightText}",
            TextWrapping = TextWrapping.Wrap
        });

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(nameBox);
        content.Children.Add(genderBox);
        content.Children.Add(weightBox);
        content.Children.Add(calledCountBox);
        content.Children.Add(readOnlyDetails);
        content.Children.Add(validationText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = member is null ? "新增成员" : "编辑成员",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Opened += (_, _) => App.GetService<AnimationService>().PlayPopupEntrance(dialog);
        dialog.Closing += (_, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
            {
                return;
            }

            if (viewModel.TryValidateMemberName(member, nameBox.Text, out var error))
            {
                return;
            }

            validationText.Text = error;
            validationText.Visibility = Visibility.Visible;
            args.Cancel = true;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var gender = (genderBox.SelectedItem as GenderOption)?.Gender;
        var calledCount = ToCalledCount(calledCountBox.Value);
        if (member is null)
        {
            await viewModel.AddMemberAsync(nameBox.Text, gender, weightBox.Value, calledCount);
        }
        else
        {
            await viewModel.UpdateMemberAsync(member, nameBox.Text, gender, weightBox.Value, calledCount);
        }
    }

    private static int ToCalledCount(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(value), 0d, int.MaxValue);
    }

    private sealed record GenderOption(MemberGender? Gender, string Label);
}
