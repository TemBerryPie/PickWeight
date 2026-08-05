using Windows.Storage;
using Windows.Storage.Pickers;

namespace PickWeight_App.Services;

public sealed class FileDialogService
{
    public async Task<string?> OpenTextAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var file = await picker.PickSingleFileAsync();
        return file is null ? null : await FileIO.ReadTextAsync(file);
    }

    public async Task<string?> OpenPathAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task SaveTextAsync(string suggestedName, string extension, string content)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await FileIO.WriteTextAsync(file, content);
        }
    }
}
