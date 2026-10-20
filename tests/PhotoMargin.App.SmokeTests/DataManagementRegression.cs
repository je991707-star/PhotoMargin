using System.IO.Compression;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.App.Views;
using PhotoMargin.Core.Models;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunDataManagementRegressionAsync(bool restore)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await UntilPresetAsync(() => RootGrid.IsLoaded); AppWindow.Resize(new SizeInt32(1344, 892));
            OpenSettings(SettingsSection.General); var page = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            var selector = (ComboBox)page.FindName("ThemeOptions");
            Check(selector.Items.Cast<string>().SequenceEqual(new[] { "跟隨系統", "淺色", "深色" }), "Single native color-mode ComboBox uses required order", checks);
            Check(((TextBox)page.FindName("DataLocationText")).IsReadOnly && ((TextBox)page.FindName("DataLocationText")).Text == ViewModel.DataDirectory,
                "Read-only data location displays actual repository path", checks);
            if (restore)
                Check(ViewModel.ThemeChoice == 2 && selector.SelectedIndex == 1 && ViewModel.ExportFormatIndex == 1 && ViewModel.OutputSize == 1440 && ViewModel.MarginRules.Single().Id == "transfer-preset",
                    "Fresh process restores imported theme, export preferences and stable preset ID", checks);
            selector.SelectedIndex = 2; await Task.Delay(160);
            Check(ViewModel.ThemeChoice == 1 && RootGrid.RequestedTheme == ElementTheme.Dark && AppWindow.TitleBar.BackgroundColor?.R == 32,
                "Dark color selection updates shared theme and native title bar", checks);
            await CaptureWindowAsync("data-general-dark.png");
            selector.SelectedIndex = 0; await Task.Delay(100); Check(ViewModel.ThemeChoice == 0, "System mode retains original stable enum mapping", checks);
            selector.SelectedIndex = 1; await Task.Delay(100);
            Check(ViewModel.ThemeChoice == 2 && RootGrid.RequestedTheme == ElementTheme.Light && AppWindow.TitleBar.BackgroundColor?.R == 249,
                "Light color selection updates title bar without duplicate toggles", checks);
            await page.RequestBackAsync();
            var photo = Environment.GetEnvironmentVariable("PHOTOMARGIN_EDITED_SAMPLE") ?? throw new InvalidOperationException("Missing JPEG fixture");
            await ViewModel.LoadPhotoAsync(photo); ViewModel.Arrangement = InformationArrangement.Stacked; ViewModel.Camera = "Data import keeps manual camera";
            await WaitForPreviewAsync(); var side = ViewModel.PhotoSideMargin; var font = ViewModel.FontFamily;
            OpenSettings(SettingsSection.General); page = (SettingsPage)SettingsFrame.Content;
            var testRoot = Directory.GetParent(ViewModel.DataDirectory)!.FullName;
            var sourceRepository = new DataRepository(new(Path.Combine(testRoot, "SourceData")));
            var source = await sourceRepository.LoadAsync();
            var preset = BuiltInMarginPresets.Create() with { Id = "transfer-preset", Name = "Transfer preset" };
            await sourceRepository.SaveAsync(source with { Theme = ThemePreference.Light, Export = new() { Format = ExportFormat.Png, JpegQuality = 81 },
                Layout = source.Layout with { PhotoSideMargin = 300, FontFamily = "Consolas", OutputSize = 1440 }, MarginRules = [preset], SelectedMarginRuleId = preset.Id });
            var bundle = Path.Combine(testRoot, "native-source.zip"); await sourceRepository.ExportBundleAsync(bundle);
            if (!restore)
            {
                await ViewModel.RuleEditor.ChangeModeAsync(PresetMode.Edit); ViewModel.RuleEditor.Side = 96;
                var pendingDraft = ViewModel.ImportSettingsBundleAsync(() => Task.FromResult<string?>(bundle), page.ConfirmSettingsImportAsync, page.ResolveImportDraftAsync);
                var draftDialog = await WaitForPresetDialogAsync("尚未儲存的預設修改");
                Check(draftDialog.PrimaryButtonText == "儲存" && draftDialog.SecondaryButtonText == "放棄" && draftDialog.IsPrimaryButtonEnabled,
                    "Unsaved draft dialog offers enabled Save, Discard and Cancel", checks);
                InvokeButton(Descendants(draftDialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
                Check(!await pendingDraft && ViewModel.RuleEditor.HasDirtyChanges && ViewModel.RuleEditor.Side == 96, "Cancelling import preserves the editor draft", checks);
            }
            var pending = ViewModel.ImportSettingsBundleAsync(() => Task.FromResult<string?>(bundle), page.ConfirmSettingsImportAsync, () => Task.FromResult(ImportDraftChoice.Discard));
            var dialog = await WaitForPresetDialogAsync("匯入設定集？");
            Check(dialog.Content as string == "將取代目前的設定與預設。匯入前會自動備份。" && dialog.DefaultButton == ContentDialogButton.Close,
                "Native import confirmation has exact text and defaults to cancel", checks);
            Check(!((Button)page.FindName("ImportSettingsBundleButton")).IsEnabled && !((Button)page.FindName("ExportSettingsBundleButton")).IsEnabled,
                "Data operations disable duplicate import and export submissions", checks);
            await CaptureWindowAsync("data-import-confirm.png");
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
            Check(!await pending && ViewModel.SourcePath == photo && ViewModel.Camera == "Data import keeps manual camera", "Cancelled confirmation preserves current work", checks);
            pending = ViewModel.ImportSettingsBundleAsync(() => Task.FromResult<string?>(bundle), page.ConfirmSettingsImportAsync, () => Task.FromResult(ImportDraftChoice.Discard));
            dialog = await WaitForPresetDialogAsync("匯入設定集？");
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            Check(await pending && ViewModel.ThemeChoice == 2 && ViewModel.MarginRules.Single().Id == "transfer-preset", "Confirmed import updates theme and full preset collection", checks);
            await WaitForPreviewAsync(); await Task.Delay(180);
            Check(ViewModel.SourcePath == photo && ViewModel.Camera == "Data import keeps manual camera" && ViewModel.PhotoSideMargin == side && ViewModel.FontFamily == font,
                "Import does not apply the imported preset or change existing work typography", checks);
            Check(MainExportFormatBox.SelectedIndex == 1 && ViewModel.OutputSize == 1440 && OutputSizeBox.SelectedIndex == 2 && CustomOutputSizeBox.Value == 1440,
                "Main format and size controls immediately reflect imported export preferences", checks);
            await CaptureWindowAsync("data-general-imported-light.png");
            var exported = Path.Combine(testRoot, "native-exported.zip"); await ViewModel.ExportSettingsBundleAsync(_ => Task.FromResult<string?>(exported));
            using (var archive = ZipFile.OpenRead(exported))
                Check(archive.Entries.Select(entry => entry.FullName).Order().SequenceEqual(new[] { "manifest.json", "presets.json", "settings.json" }), "Exported bundle contains exactly three portable JSON documents", checks);
            await page.RequestBackAsync(); await Task.Delay(150);
            Check(WorkspaceGrid.Visibility == Visibility.Visible && ViewModel.HasPhoto && ViewModel.Camera == "Data import keeps manual camera", "Settings back returns to the same work", checks);
            await CaptureWindowAsync("data-main-preserved.png"); await ViewModel.FlushSettingsAsync();
            Check(Directory.GetDirectories(Path.Combine(ViewModel.DataDirectory, "Backups")).Length <= 5, "At most five backup groups retained", checks);
        }
        catch (Exception error) { failure = error; await CaptureWindowAsync("data-ui-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, restore ? "data-restore-result.json" : "data-regression-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, DataDirectory = ViewModel.DataDirectory, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
