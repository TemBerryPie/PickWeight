using PickWeight_App.ViewModels;

namespace PickWeight.App.Tests;

/// <summary>
/// 计划中 UI 补全项的契约测试：先锁定公开 API 形态，避免后续改动悄悄丢掉。
/// </summary>
public sealed class PageViewModelContractTests
{
    [Fact]
    public void MembersSortOptions_IncludeSeatAndMostRecentDrawn()
    {
        Assert.Contains("座位", MembersPageViewModel.SortOptions);
        Assert.Contains("最近被点", MembersPageViewModel.SortOptions);
    }

    [Fact]
    public void SeatMapPageViewModel_ImplementsEditablePageContract()
    {
        Assert.True(
            typeof(IEditablePageViewModel).IsAssignableFrom(typeof(SeatMapPageViewModel)),
            "SeatMapPageViewModel 应实现 IEditablePageViewModel，让主页面在离开前提示未保存修改。");
    }
}
