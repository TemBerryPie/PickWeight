using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace PickWeight_App.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel
}

public sealed class DialogService
{
    public async Task ShowAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定"
        };
        await dialog.ShowAsync();
    }

    public async Task<UnsavedChoice> ConfirmUnsavedAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot,
            Title = "存在未保存的修改",
            Content = "是否保存当前页面的修改？",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "放弃",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChoice.Save,
            ContentDialogResult.Secondary => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel
        };
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<bool> ConfirmContentAsync(string title, object content, string confirmButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
