using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

public sealed class FileDialogService(Window owner)
{
    public async Task<string?> PickInputAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public Task<string?> PickOutputAsync(string suggestedName) => PickOutputAsync(ExportFormat.Jpg, suggestedName);
    public async Task<string?> PickOutputAsync(ExportFormat format, string suggestedName)
    {
        // The desktop SDK picker returns only a path. The legacy UWP picker
        // creates a zero-byte placeholder, which breaks numbering and cancellation.
        var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = suggestedName,
            DefaultFileExtension = new ExportPreferences { Format = format }.Extension,
            ShowOverwritePrompt = false
        };
        var preferences = new ExportPreferences { Format = format };
        picker.FileTypeChoices.Add(format + " 圖片", [preferences.Extension]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> PickExportDirectoryAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public async Task OpenDirectoryAsync(string path)
    {
        if (!await Windows.System.Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(path)))
            throw new IOException("系統未能開啟資料夾。");
    }

    public async Task<string?> PickSettingsOutputAsync(string suggestedName)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(owner.AppWindow.Id)
        { SuggestedFileName = suggestedName, DefaultFileExtension = ".zip", ShowOverwritePrompt = true };
        picker.FileTypeChoices.Add("PhotoMargin 設定集", [".zip"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
    public async Task<string?> PickSettingsInputAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(owner.AppWindow.Id);
        picker.FileTypeFilter.Add(".zip");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickRawInformationAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.List,
            CommitButtonText = "請選擇同一張照片的 RAW"
        };
        foreach (var extension in PhotoMargin.Imaging.RawMetadataReader.SupportedExtensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
