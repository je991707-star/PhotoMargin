using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.ViewModels;

namespace PhotoMargin.App.Views;

public sealed partial class SettingsPage
{
    private async void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { if (viewModel.CanManageData && fileDialogs is not null) await fileDialogs.OpenDirectoryAsync(viewModel.DataDirectory); }
        catch (Exception error) { viewModel.ReportDataFailure(error); }
    }
    private async void ExportSettingsBundle_Click(object sender, RoutedEventArgs e)
    { if (fileDialogs is not null) await viewModel.ExportSettingsBundleAsync(fileDialogs.PickSettingsOutputAsync); }
    private async void ImportSettingsBundle_Click(object sender, RoutedEventArgs e)
    { if (fileDialogs is not null) await viewModel.ImportSettingsBundleAsync(fileDialogs.PickSettingsInputAsync, ConfirmSettingsImportAsync, ResolveImportDraftAsync); }

    internal async Task<bool> ConfirmSettingsImportAsync()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "匯入設定集？",
            Content = "將取代目前的設定與預設。匯入前會自動備份。", PrimaryButtonText = "匯入", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    internal async Task<ImportDraftChoice> ResolveImportDraftAsync()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "尚未儲存的預設修改",
            Content = "請先處理編輯草稿。", PrimaryButtonText = "儲存", SecondaryButtonText = "放棄", CloseButtonText = "取消匯入",
            IsPrimaryButtonEnabled = viewModel.RuleEditor.CanSave, DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() switch { ContentDialogResult.Primary => ImportDraftChoice.Save,
            ContentDialogResult.Secondary => ImportDraftChoice.Discard, _ => ImportDraftChoice.Cancel };
    }
}
