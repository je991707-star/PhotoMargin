using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

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

    public async Task<string?> PickOutputAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add("JPEG 圖片", [".jpg"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
