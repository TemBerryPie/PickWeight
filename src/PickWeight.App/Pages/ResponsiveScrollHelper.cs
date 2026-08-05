using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace PickWeight_App.Pages;

internal static class ResponsiveScrollHelper
{
    public static void Apply(
        Page page,
        ScrollViewer pageScrollViewer,
        FrameworkElement scrollOwner,
        double minHeight = 220,
        bool forceInnerScroll = false)
    {
        var isMaximized = IsMainWindowMaximized();
        var useInnerScroll = forceInnerScroll || isMaximized;
        var usePageScroll = !isMaximized;
        pageScrollViewer.VerticalScrollMode = usePageScroll ? ScrollMode.Enabled : ScrollMode.Disabled;
        pageScrollViewer.VerticalScrollBarVisibility = usePageScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        if (useInnerScroll)
        {
            scrollOwner.Height = Math.Max(minHeight, GetAvailableHeight(page, scrollOwner));
        }
        else
        {
            scrollOwner.Height = double.NaN;
        }

        if (scrollOwner is ListViewBase list)
        {
            ScrollViewer.SetVerticalScrollMode(list, useInnerScroll ? ScrollMode.Enabled : ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, useInnerScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        }
    }

    private static double GetAvailableHeight(Page page, FrameworkElement element)
    {
        if (page.ActualHeight <= 0)
        {
            return 320;
        }

        var top = element.TransformToVisual(page).TransformPoint(new Point()).Y;
        return page.ActualHeight - top - 28;
    }

    private static bool IsMainWindowMaximized()
    {
        var handle = App.WindowHandle;
        return handle != 0 && IsZoomed(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint windowHandle);
}
