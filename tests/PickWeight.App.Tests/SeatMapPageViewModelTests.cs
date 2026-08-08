using Microsoft.UI.Xaml;
using PickWeight_App.ViewModels;

namespace PickWeight.App.Tests;

/// <summary>座位表页状态 VM 的纯属性断言（构造无副作用）。</summary>
public sealed class SeatMapPageViewModelTests
{
    [Fact]
    public void HasUnassignedVisibility_VisibleWhenUnassignedExist()
    {
        var viewModel = new SeatMapPageViewModel();
        viewModel.HasUnassigned = true;

        Assert.Equal(Visibility.Visible, viewModel.HasUnassignedVisibility);
    }

    [Fact]
    public void HasUnassignedVisibility_CollapsedWhenNoUnassigned()
    {
        var viewModel = new SeatMapPageViewModel();
        viewModel.HasUnassigned = false;

        Assert.Equal(Visibility.Collapsed, viewModel.HasUnassignedVisibility);
    }
}
