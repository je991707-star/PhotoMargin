using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.App.Views;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunRawInformationRegressionAsync(bool restore)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await UntilPresetAsync(() => RootGrid.IsLoaded); await Task.Delay(200);
            AppWindow.Resize(new SizeInt32(1344, 1000)); ViewModel.ThemeChoice = 1;
            var raw = Environment.GetEnvironmentVariable("PHOTOMARGIN_RW2_SAMPLE") ?? throw new InvalidOperationException("Missing RW2 fixture environment variable.");
            var jpeg = Environment.GetEnvironmentVariable("PHOTOMARGIN_EDITED_SAMPLE") ?? throw new InvalidOperationException("Missing JPEG fixture environment variable.");
            var rawHash = await HashRawFixtureAsync(raw); var jpegHash = await HashRawFixtureAsync(jpeg);
            OpenSettings(SettingsSection.Camera); var page = (SettingsPage)SettingsFrame.Content;
            await Task.Delay(200);
            var navigation = (NavigationView)page.FindName("SettingsNavigation");
            Check(navigation.MenuItems.OfType<NavigationViewItem>().Select(item => item.Tag?.ToString())
                .SequenceEqual(new[] { "General", "Layout", "Camera", "Export" }), "Camera settings restored in original navigation position", checks);
            Check(!((Button)page.FindName("ReadRawInformationButton")).IsEnabled && !ViewModel.HasPhoto,
                "No-photo state cannot create a RAW-only work", checks);
            Check(InformationValue(page, InformationField.Lens) == "未提供" &&
                !((Button)page.FindName("RestorePhotoExifButton")).IsEnabled,
                "No-photo EXIF is readable missing text, not empty input boxes", checks);
            await page.RequestBackAsync(); await ViewModel.LoadPhotoAsync(jpeg); ViewModel.Arrangement = InformationArrangement.Stacked;
            ViewModel.OutputSize = 1080; ViewModel.ShowGuides = false; await WaitForPreviewAsync();
            var summary = ViewModel.SourceSummary;
            OpenSettings(SettingsSection.Camera); page = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            Check(Descendants((ItemsControl)page.FindName("CameraInformationRows")).OfType<TextBlock>().Any(text => text.Text == ViewModel.Lens) &&
                ViewModel.LensInformationSource == (restore ? "RAW 補入" : "JPG EXIF"), "Actual text template displays the lens and explicit RAW/JPG provenance", checks);
            if (restore)
            {
                Check(ViewModel.ConfirmedRaw is not null && !File.Exists(ViewModel.ConfirmedRaw.SourcePath) &&
                    ViewModel.Lens == "OLYMPUS M.12-40mm F2.8", "Fresh process restores confirmed snapshot without opening the moved RAW", checks);
                Check(ViewModel.InformationSource(InformationField.Lens) == InformationOrigin.Raw &&
                    InformationValue(page, InformationField.Lens) == ViewModel.Lens && LensTextBox.Text == ViewModel.Lens,
                    "Restart preserves shared field values and RAW provenance", checks);
                await CaptureWindowAsync("raw-camera-restored.png");
                await ViewModel.ExportAsync(Path.Combine(AppContext.BaseDirectory, "raw-restored-export.jpg"));
                Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "raw-restored-export.jpg")), "Restored snapshot exports with no RAW file", checks);
                return;
            }
            var ownedRaw = Path.Combine(AppContext.BaseDirectory, "raw-test-input.rw2"); File.Copy(raw, ownedRaw, true);
            ViewModel.Camera = "Manual camera preserved"; ViewModel.IsoValue = 800; await WaitForPreviewAsync();
            var pending = page.ImportRawInformationAsync(ownedRaw);
            var dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            Check(RawChoices(dialog).Count() == 8 && RawChoice(dialog, InformationField.Camera, 0).IsChecked == true &&
                RawChoice(dialog, InformationField.Date, 1).IsChecked == true && dialog.DefaultButton == ContentDialogButton.Close,
                "Eight independent field groups retain existing values, fill missing dates, and default the dialog to Cancel", checks);
            Check(!Descendants(dialog).OfType<RadioButtons>().Any(control => control.Name == "RawMergeModeOptions"),
                "The former global merge-mode entrance is removed", checks);
            Check(Descendants(dialog).OfType<TextBlock>().Any(text => text.Text.Contains("RAW：", StringComparison.Ordinal)),
                "Native review displays incoming RAW values and current differences", checks);
            await CaptureWindowAsync("raw-information-review.png");
            var fieldScroller = Descendants(dialog).OfType<ScrollViewer>().Single(control => control.Name == "RawFieldChoicesScroller");
            fieldScroller.ChangeView(null, fieldScroller.ScrollableHeight, null, true); await Task.Delay(100);
            Check(ElementBounds(RawChoice(dialog, InformationField.Iso, 1)).Bottom <=
                ElementBounds(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton")).Top,
                "Last field remains reachable through native scrolling above the fixed action footer", checks);
            AppWindow.Resize(new SizeInt32(800, 700)); await Task.Delay(200);
            Check(fieldScroller.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed &&
                ElementBounds(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton")).Bottom <= RootGrid.ActualHeight,
                "Small-window review preserves vertical-only scrolling and visible Cancel action", checks);
            await CaptureWindowAsync("raw-review-small.png");
            AppWindow.Resize(new SizeInt32(1344, 1000)); await Task.Delay(150);
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
            Check(!await pending && ViewModel.ConfirmedRaw is null && ViewModel.Camera == "Manual camera preserved",
                "Cancelling actual native review leaves all current values unchanged", checks);
            pending = page.ImportRawInformationAsync(ownedRaw); dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            Check(await pending, "Actual native fill-missing confirmation applies metadata", checks); await WaitForPreviewAsync();
            Check(ViewModel.Camera == "Manual camera preserved" && ViewModel.IsoValue == 800 &&
                ViewModel.Lens == "M.Zuiko Digital ED 12-40mm F2.8 Pro", "Fill missing preserves manual camera/ISO and existing JPEG lens", checks);
            Check(ViewModel.SelectedDate?.Date == new DateTime(2026, 4, 15) &&
                ViewModel.InformationSource(InformationField.Date) == InformationOrigin.Raw, "RAW capture time replaces file-creation fallback with RAW provenance", checks);
            ViewModel.ThemeChoice = 2;
            pending = page.ImportRawInformationAsync(ownedRaw); dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            ChooseRaw(dialog, InformationField.Lens); await CaptureWindowAsync("raw-review-light.png");
            Check(RawChoice(dialog, InformationField.Camera, 0).IsChecked == true && RawChoice(dialog, InformationField.Iso, 0).IsChecked == true,
                "Selecting RAW for lens does not deselect current camera or ISO in other groups", checks);
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            Check(await pending && ViewModel.Lens == "OLYMPUS M.12-40mm F2.8" && ViewModel.Camera == "Manual camera preserved" && ViewModel.IsoValue == 800,
                "Per-field confirmation applies only the chosen RAW lens and preserves manual camera and ISO", checks);
            await WaitForPreviewAsync(); ViewModel.ThemeChoice = 1;
            pending = page.ImportRawInformationAsync(ownedRaw); dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            foreach (var field in Enum.GetValues<InformationField>()) ChooseRaw(dialog, field);
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            Check(await pending, "Native override mode applies only RAW-provided values", checks); await WaitForPreviewAsync();
            Check(ViewModel.Camera == "Panasonic DC-GH5" && ViewModel.Lens == "OLYMPUS M.12-40mm F2.8" && ViewModel.IsoValue == 500 &&
                ViewModel.FocalLengthValue == 31 && ViewModel.ApertureValue == 3.2 && ViewModel.Shutter == "1/60s", "GH5 real RW2 fields match expected metadata", checks);
            Check(InformationValue(page, InformationField.Lens) == ViewModel.Lens && LensTextBox.Text == ViewModel.Lens &&
                ViewModel.InformationSource(InformationField.Lens) == InformationOrigin.Raw, "Settings and main native controls share values and provenance", checks);
            Check(Descendants((ItemsControl)page.FindName("CameraInformationRows")).OfType<TextBlock>().Any(text => text.Text == ViewModel.Lens) &&
                ((TextBlock)page.FindName("AppliedInformationFileText")).Text.Contains("RAW", StringComparison.Ordinal),
                "Visible plain EXIF text refreshes after merge and names the active RAW file", checks);
            await CaptureWindowAsync("raw-camera-settings.png");
            ViewModel.ThemeChoice = 2; AppWindow.Resize(new SizeInt32(800, 700)); await Task.Delay(250);
            Check(((ScrollViewer)page.FindName("CommonSettingsScrollViewer")).ComputedHorizontalScrollBarVisibility == Visibility.Collapsed,
                "Read-only information stays vertically scrollable without horizontal scrollbar in a small light window", checks);
            Check(Descendants((ItemsControl)page.FindName("CameraInformationRows")).OfType<TextBlock>()
                .All(text => ElementBounds(text).Right <= ElementBounds((ScrollViewer)page.FindName("CommonSettingsScrollViewer")).Right - 8),
                "Every value and provenance text stays inside the small-window viewport instead of being clipped", checks);
            await CaptureWindowAsync("raw-camera-small-light.png");
            ViewModel.ThemeChoice = 1; AppWindow.Resize(new SizeInt32(1344, 892)); await Task.Delay(200);
            Check(!Descendants((DependencyObject)page.FindName("CameraContent")).Any(control => control is TextBox or NumberBox or CalendarDatePicker or TimePicker),
                "Camera settings uses plain text only; editing controls exist only on main", checks);
            CameraTextBox.Text = "Manual through main"; await WaitForPreviewAsync();
            Check(ViewModel.Camera == "Manual through main" && InformationValue(page, InformationField.Camera) == ViewModel.Camera &&
                ViewModel.InformationSource(InformationField.Camera) == InformationOrigin.Manual, "Editing main immediately updates the read-only information page and provenance", checks);
            Check(!await ViewModel.ReloadExifAsync(() => Task.FromResult(false)) && ViewModel.Camera == "Manual through main",
                "Photo Reload cancellation preserves RAW and manual information", checks);
            Check(await ViewModel.ReloadExifAsync(() => Task.FromResult(true)) && ViewModel.Lens == "OLYMPUS M.12-40mm F2.8" && ViewModel.ConfirmedRaw is not null,
                "Confirmed photo Reload refreshes source EXIF but retains confirmed RAW layer", checks);
            await WaitForPreviewAsync(); await ViewModel.FlushSettingsAsync();
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ui-raw-information.json"), Path.Combine(AppContext.BaseDirectory, "ui-raw-restore.json"), true);
            File.Move(ownedRaw, ownedRaw + ".moved", true);
            Check(!await ViewModel.ReloadRawInformationAsync(_ => Task.FromResult<RawMergeMode?>(RawMergeMode.OverwriteProvided)) &&
                ViewModel.Lens == "OLYMPUS M.12-40mm F2.8", "Missing RAW blocks only re-read and never clears confirmed fields", checks);
            await page.RequestBackAsync(); await WaitForPreviewAsync();
            Check(WorkspaceGrid.Visibility == Visibility.Visible && ViewModel.SourcePath == Path.GetFullPath(jpeg) && ViewModel.SourceSummary == summary,
                "One back returns the existing JPEG composition without orientation/dimension changes", checks);
            await ViewModel.ExportAsync(Path.Combine(AppContext.BaseDirectory, "raw-merged-export.jpg"));
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "raw-merged-export.jpg")), "Actual JPEG export succeeds after RAW is moved", checks);
            await Task.Delay(300); RootGrid.UpdateLayout();
            await CaptureWindowAsync("raw-merged-main.png");
            OpenSettings(SettingsSection.Camera); page = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            var restorePending = page.RestorePhotoExifAsync(); var restoreDialog = await WaitForPresetDialogAsync(ViewModel.RestorePhotoExifLabel + "？");
            Check(restoreDialog.DefaultButton == ContentDialogButton.Close, "Restore native confirmation defaults to Cancel", checks);
            InvokeButton(Descendants(restoreDialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
            Check(!await restorePending && ViewModel.ConfirmedRaw is not null, "Cancelling restore retains the RAW snapshot", checks);
            restorePending = page.RestorePhotoExifAsync(); restoreDialog = await WaitForPresetDialogAsync(ViewModel.RestorePhotoExifLabel + "？");
            InvokeButton(Descendants(restoreDialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            Check(await restorePending && ViewModel.ConfirmedRaw is null && ViewModel.Lens == ViewModel.PhotoExif.LensModel &&
                ViewModel.LensInformationSource == "JPG EXIF" && ViewModel.SourcePath == Path.GetFullPath(jpeg),
                "Confirmed restore clears RAW and manual values but retains the same JPEG composition", checks);
            await WaitForPreviewAsync(); await Task.Delay(150); await CaptureWindowAsync("camera-jpg-restored.png");
            pending = page.ImportRawInformationAsync(raw); dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            Check(((TextBlock)RawChoice(dialog, InformationField.Camera, 0).Content).Text.StartsWith("JPG：", StringComparison.Ordinal),
                "Current photo EXIF is labelled JPG, not confused with manual or previous RAW values", checks);
            await CaptureWindowAsync("raw-review-jpg-current.png");
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton")); await pending;
            var partialReview = new RawInformationReview(raw, new PhotoMetadata { Model = "Partial RAW" },
                new InformationValues { Lens = ViewModel.Lens }, new Dictionary<InformationField, InformationOrigin>(), ViewModel.PhotoExif);
            var partialConfirmation = page.ConfirmRawInformationAsync(partialReview); dialog = await WaitForPresetDialogAsync("確認 RAW 拍攝資訊");
            Check(!RawChoice(dialog, InformationField.Lens, 1).IsEnabled && RawChoice(dialog, InformationField.Lens, 0).IsChecked == true,
                "RAW missing a field disables its native radio and retains the current option", checks);
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
            Check(await partialConfirmation is null, "Cancelling a field review produces no selection to commit", checks);
            await page.RequestBackAsync();
            var other = Path.Combine(AppContext.BaseDirectory, "raw-test-other.png"); using (var image = new MagickImage(MagickColors.White, 1200, 800)) image.Write(other);
            await ViewModel.LoadPhotoAsync(other); await WaitForPreviewAsync();
            Check(ViewModel.ConfirmedRaw is null && ViewModel.Lens.Length == 0, "Replacing the work clears the preceding RAW association and imported values", checks);
            Check(rawHash == await HashRawFixtureAsync(raw) && jpegHash == await HashRawFixtureAsync(jpeg), "Both user-provided original files remain byte-identical", checks);
            await ViewModel.FlushSettingsAsync();
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("raw-information-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, restore ? "raw-information-restore-result.json" : "raw-information-result.json"),
                JsonSerializer.Serialize(new { Passed = failure is null, Checks = checks.Count,
                    Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                    new JsonSerializerOptions { WriteIndented = true })); Close();
        }
    }
    private static async Task<string> HashRawFixtureAsync(string path)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
    private static string InformationValue(SettingsPage page, InformationField field) =>
        ((ItemsControl)page.FindName("CameraInformationRows")).Items.Cast<PhotoInformationRow>().Single(row => row.Field == field).Value;
    private static IEnumerable<RadioButtons> RawChoices(ContentDialog dialog) =>
        Descendants(dialog).OfType<RadioButtons>().Where(control => control.Name.StartsWith("RawField", StringComparison.Ordinal));
    private static RadioButton RawChoice(ContentDialog dialog, InformationField field, int index) =>
        (RadioButton)RawChoices(dialog).Single(control => control.Name == "RawField" + field).Items[index];
    private static void ChooseRaw(ContentDialog dialog, InformationField field)
    {
        var radio = RawChoice(dialog, field, 1);
        if (!radio.IsEnabled) return;
        ((Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider)
            new Microsoft.UI.Xaml.Automation.Peers.RadioButtonAutomationPeer(radio)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem)).Select();
    }
}
