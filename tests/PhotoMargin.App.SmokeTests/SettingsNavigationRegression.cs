using System.Runtime.InteropServices;
using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoMargin.App.Services;
using PhotoMargin.App.Views;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PhotoMargin.App;

// Actual WinUI integration checks, isolated preferences and fixtures; Debug only.
public sealed partial class MainWindow
{
    internal async Task RunArrangementRestoreRegressionAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await Task.Delay(200);
            Check(ViewModel.Arrangement == InformationArrangement.Stacked &&
                  ArrangementBox.SelectedItem is ViewModels.ArrangementChoice { Value: InformationArrangement.Stacked } && ArrangementBox.SelectedIndex == 2,
                "New process restores persisted arrangement in actual native ComboBox", checks);
            Check(ViewModel.PhotoSideMargin == 64 && ViewModel.MarginRules.Any(rule => rule.Id == ViewModel.SelectedMarginRuleId),
                "New process preserves selected margin rule", checks);
            Check(ViewModel.OutputSize == 1080 && ViewModel.JpegQuality == 93 && ViewModel.LensFontSize == 24 && ViewModel.DateFontSize == 20,
                "New process preserves output settings and independent font sizes", checks);
            Check(!ViewModel.HasPhoto && !ArrangementBox.IsEnabled && SelectPhotoButton.IsEnabled,
                "Restart restores preferences without inventing a source photo", checks);
            SettingsScrollViewer.ChangeView(null, 300, null, true); await Task.Delay(100);
            await CaptureWindowAsync("arrangement-restarted.png");
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "arrangement-restore-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
    internal async Task RunSettingsNavigationRegressionAsync()
    {
        var checks = new List<object>();
        Exception? failure = null;
        try
        {
            await Initialization;
            ViewModel.ThemeChoice = 1;
            await Task.Delay(150);
            Check(!ViewModel.CanReload && !ReloadExifButton.IsEnabled && SelectPhotoButton.IsEnabled && !ExportButton.IsEnabled && SettingsButton.IsEnabled, "Empty state import/settings available; export/reload disabled", checks);
            Check(EmptyPhotoState.Visibility == Visibility.Visible && PreviewViewport.Visibility == Visibility.Collapsed &&
                  ChangePhotoButton.Visibility == Visibility.Collapsed && SourceSummaryText.Visibility == Visibility.Collapsed,
                "Empty state shows import prompt, not white canvas or duplicate change-photo action", checks);
            Check(!SidebarSettingsHost.IsEnabled && Descendants(SidebarContent).OfType<Control>().All(control => !control.IsEnabled),
                "All sidebar controls are natively disabled without changing their content", checks);
            Check(RootGrid.FindName("ImportButton") is null && RootGrid.FindName("ResetLayoutButton") is null, "Removed old UI entrances", checks);
            Check(!BrandImageBox.IsEnabled && BrandImageBox.Items.Count == 1, "Brand images explicitly disabled", checks);
            Check(ReloadExifButton.Content is SymbolIcon { Symbol: Symbol.Refresh }, "Only icon in Reload button", checks);
            Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(ReloadExifButton) == "重新讀取照片資訊", "Reload accessibility name", checks);
            await VerifyMainLayoutAsync(checks, "1344x892-dark");
            await CaptureWorkspaceAsync("figma-main-empty.png");
            await CaptureWindowAsync("figma-main-window-empty.png");
            ViewModel.ThemeChoice = 2; await Task.Delay(150);
            Check(!OutputSizeBox.IsEnabled && !JpegQualitySlider.IsEnabled && !AddRuleButton.IsEnabled && SelectPhotoButton.IsEnabled,
                "Empty state remains disabled and importable in light theme", checks);
            await CaptureWindowAsync("empty-state-light-window.png");
            ViewModel.ThemeChoice = 1; await Task.Delay(100);
            await ViewModel.LoadPhotoAsync(Path.Combine(AppContext.BaseDirectory, "missing-empty-state-test.jpg"));
            Check(!ViewModel.HasPhoto && EmptyPhotoState.Visibility == Visibility.Visible && !SidebarSettingsHost.IsEnabled &&
                  SourceSummaryText.Visibility == Visibility.Visible && SelectPhotoButton.IsEnabled,
                "Failed initial import retains empty state, disabled settings and visible error", checks);

            OutputSizeBox.SelectedIndex = 1;
            Check(ViewModel.OutputSize == 2048, "Output size bound", checks);
            OutputSizeBox.SelectedIndex = 2;
            ViewModel.OutputSize = 1500;
            Check(CustomOutputSizeBox.Visibility == Visibility.Visible && OutputSizeBox.SelectedIndex == 2, "Custom size bound", checks);
            OutputSizeBox.SelectedIndex = 0;
            ViewModel.JpegQuality = 93;

            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
            var sample = Path.Combine(root, "samples", "input", "sample_3x2.png");
            var fixture = Path.Combine(AppContext.BaseDirectory, "figma-exif-source.jpg");
            using (var input = new MagickImage(sample))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.Make, "Panasonic"); exif.SetValue(ExifTag.Model, "DC-GH5");
                exif.SetValue(ExifTag.LensModel, "LEICA DG 12-60mm F2.8-4.0");
                exif.SetValue(ExifTag.DateTimeOriginal, "2023:11:08 16:15:20");
                exif.SetValue(ExifTag.FocalLength, new Rational(35, 1));
                exif.SetValue(ExifTag.FNumber, new Rational(28, 10));
                exif.SetValue(ExifTag.ExposureTime, new Rational(1, 250));
                exif.SetValue(ExifTag.ISOSpeedRatings, new ushort[] { 200 });
                input.SetProfile(exif); input.Write(fixture);
            }
            await ViewModel.LoadPhotoAsync(fixture); await WaitForPreviewAsync();
            Check(EmptyPhotoState.Visibility == Visibility.Collapsed && PreviewViewport.Visibility == Visibility.Visible &&
                  ChangePhotoButton.Visibility == Visibility.Visible && SourceSummaryText.Visibility == Visibility.Visible && SidebarSettingsHost.IsEnabled &&
                  OutputSizeBox.IsEnabled && JpegQualitySlider.IsEnabled && CameraTextBox.IsEnabled && AddRuleButton.IsEnabled && !BrandImageBox.IsEnabled,
                "Successful import restores existing preview/settings/actions while brand remains disabled", checks);
            Check(ViewModel.Camera == "Panasonic DC-GH5" && CameraTextBox.Text == ViewModel.Camera, "Imported camera bound", checks);
            Check(PhotoDatePicker.Date is { Year: 2023, Month: 11, Day: 8 } && PhotoTimePicker.SelectedTime is { Hours: 16, Minutes: 15 }, "EXIF date and time bound", checks);
            Check(!ViewModel.HasInformationOverrides && ViewModel.CanReload, "Original information has no overrides", checks);
            Check(ViewModel.FocalLengthValue == 35 && ViewModel.ApertureValue == 2.8 && ViewModel.IsoValue == 200 && ViewModel.ShutterInput == "1/250", "EXIF numeric inputs bound", checks);
            ViewModel.LensFontSize = 24; ViewModel.DateFontSize = 20;
            await WaitForPreviewAsync();
            Check((double)LensFontSizeBox.SelectedItem == 24 && (double)DateFontSizeBox.SelectedItem == 20, "Independent lens/time font selectors", checks);
            await CaptureWorkspaceAsync("figma-main-photo.png");
            await CaptureWindowAsync("figma-main-window.png");
            await VerifyArrangementAsync(checks);

            var state = LayoutState();
            ViewModel.Camera = "Manual camera"; ViewModel.Lens = "Manual lens";
            ViewModel.SelectedDate = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero);
            ViewModel.SelectedTime = new TimeSpan(9, 30, 0);
            ViewModel.FocalLengthValue = 50; ViewModel.ApertureValue = 4; ViewModel.ShutterInput = "1/125"; ViewModel.IsoValue = 800;
            Check(ViewModel.HasInformationOverrides, "Manual changes detected", checks);
            var nativeConfirmation = ReloadPhotoInformationAsync();
            await Task.Delay(200);
            var confirmationDialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child).Prepend(popup.Child)).OfType<ContentDialog>().First();
            Check(confirmationDialog.Title?.ToString() == "重新讀取照片資訊？" && !ViewModel.CanReload,
                "Actual Reload entrance displays native overwrite confirmation", checks);
            confirmationDialog.Hide();
            Check(!await nativeConfirmation && ViewModel.Camera == "Manual camera" && ViewModel.IsoValue == 800,
                "Closing actual confirmation preserves manual information", checks);
            var confirmations = 0;
            Check(!await ViewModel.ReloadExifAsync(() => { confirmations++; return Task.FromResult(false); }), "Reload cancel result", checks);
            Check(confirmations == 1 && ViewModel.Camera == "Manual camera" && ViewModel.IsoValue == 800, "Reload cancellation preserves manual data", checks);
            var confirmGate = new TaskCompletionSource<bool>();
            var reload = ViewModel.ReloadExifAsync(() => confirmGate.Task);
            Check(!ViewModel.CanReload && !ReloadExifButton.IsEnabled, "Reload disabled during confirmation/read", checks);
            confirmGate.SetResult(true);
            Check(await reload, "Reload confirmed", checks);
            await WaitForPreviewAsync();
            Check(ViewModel.Camera == "Panasonic DC-GH5" && ViewModel.Lens.StartsWith("LEICA", StringComparison.Ordinal) && ViewModel.FocalLengthValue == 35 && ViewModel.IsoValue == 200, "Reload restores source information", checks);
            Check(LayoutState().Equals(state), "Reload preserves all layout/export/guide settings", checks);
            var noConfirmation = true;
            Check(await ViewModel.ReloadExifAsync(() => { noConfirmation = false; return Task.FromResult(true); }) && noConfirmation, "Unchanged information reloads without confirmation", checks);
            await WaitForPreviewAsync();

            var beforeRules = ViewModel.MarginRules.Count;
            ViewModel.Camera = "Keep manual camera"; await WaitForPreviewAsync();
            AddRule_Click(AddRuleButton, new RoutedEventArgs());
            var page = (SettingsPage)SettingsFrame.Content;
            Check(page.SelectedSection == SettingsSection.Layout && ((FrameworkElement)page.FindName("LayoutContent")).Visibility == Visibility.Visible &&
                  ((NavigationViewItem)((NavigationView)page.FindName("SettingsNavigation")).SelectedItem).Tag.ToString() == "Layout", "Add rule directly selects matching layout content", checks);
            Check(ViewModel.MarginRules.Count == beforeRules && LayoutState().Equals(state), "Entering layout does not create/change rules", checks);
            await Task.Delay(600);
            await CaptureWorkspaceAsync("figma-settings-layout.png");
            await CaptureWindowAsync("figma-settings-layout-window.png");
            ViewModel.RuleEditor.New(); ViewModel.RuleEditor.Name = "QA margin rule"; ViewModel.RuleEditor.Side = 64;
            Check(ViewModel.RuleEditor.Save(), "Save new margin rule", checks);
            var rule = ViewModel.MarginRules.First(item => item.Name == "QA margin rule");
            Check(ViewModel.SelectedMarginRuleId == MarginRule.DefaultId && ViewModel.PhotoSideMargin == 48, "Saving another rule keeps existing selection", checks);
            page.RequestBack(); await Task.Delay(100);
            Check(WorkspaceGrid.Visibility == Visibility.Visible && ViewModel.SourcePath == fixture && ViewModel.Camera == "Keep manual camera", "Direct layout return preserves work", checks);
            ViewModel.SelectedMarginRuleId = rule.Id; await WaitForPreviewAsync();
            Check(ViewModel.PhotoSideMargin == 64 && MarginRuleBox.SelectedValue?.ToString() == rule.Id, "Main rule selection applies stored margins", checks);
            var selected = ViewModel.SelectedMarginRuleId;
            AddRule_Click(AddRuleButton, new RoutedEventArgs());
            ((SettingsPage)SettingsFrame.Content).RequestBack(); await Task.Delay(50);
            Check(ViewModel.SelectedMarginRuleId == selected && ViewModel.Camera == "Keep manual camera", "Navigation retains selected rule and manual data", checks);

            Settings_Click(SettingsButton, new RoutedEventArgs()); page = (SettingsPage)SettingsFrame.Content;
            await Task.Delay(600);
            var navigation = (NavigationView)page.FindName("SettingsNavigation");
            var back = (Button)page.FindName("SettingsBackButton");
            var toggle = (Button)page.FindName("SettingsPaneToggleButton");
            Check(back.Visibility == Visibility.Visible && Math.Abs(ElementBounds(back).Top - ElementBounds(toggle).Top) < 1 && ElementBounds(toggle).Left > ElementBounds(back).Left,
                "Back and pane toggle match the reference's single header row", checks);
            InvokeButton(toggle); await Task.Delay(250);
            Check(!navigation.IsPaneOpen && ((FrameworkElement)page.FindName("SettingsHeaderButtons")).Visibility == Visibility.Collapsed && navigation.IsPaneToggleButtonVisible,
                $"Header toggle collapses pane and exposes native reopening control (open={navigation.IsPaneOpen}, header={((FrameworkElement)page.FindName("SettingsHeaderButtons")).Visibility}, nativeToggle={navigation.IsPaneToggleButtonVisible}, mode={navigation.DisplayMode})", checks);
            var nativeToggle = Descendants(navigation).OfType<Button>().First(button => button.Name == "TogglePaneButton");
            InvokeButton(nativeToggle); await Task.Delay(250);
            Check(navigation.IsPaneOpen && toggle.Visibility == Visibility.Visible, "Native toggle reopens reference header", checks);
            Check(navigation.MenuItems.Count == 4 && !navigation.IsSettingsVisible && page.SelectedSection == SettingsSection.General, "Four settings destinations; no duplicate Settings item", checks);
            var search = (AutoSuggestBox)page.FindName("SettingsSearch");
            search.Text = "版面"; await Task.Delay(400);
            Check(navigation.MenuItems.OfType<NavigationViewItem>().Count(item => item.Visibility == Visibility.Visible) == 1, "Settings search filters navigation", checks);
            search.Text = "";
            await Task.Delay(400);
            await CaptureWorkspaceAsync("figma-settings-general.png");
            await CaptureWindowAsync("figma-settings-window.png");
            ((RadioButtons)page.FindName("ThemeOptions")).SelectedIndex = 2;
            Check(RootGrid.RequestedTheme == ElementTheme.Light, "Settings theme applies to common state", checks);
            var homeState = LayoutState(); var homePreview = ViewModel.PreviewBytes;
            var returnEvents = 0; page.BackRequested += (_, _) => returnEvents++;
            page.NavigateTo(SettingsSection.Layout); page.NavigateTo(SettingsSection.General);
            page.NavigateTo(SettingsSection.Camera); page.NavigateTo(SettingsSection.Export);
            InvokeButton(back); await Task.Delay(100);
            Check(WorkspaceGrid.Visibility == Visibility.Visible && SettingsFrame.Content is null && SettingsFrame.Visibility == Visibility.Collapsed,
                "One custom back click returns home after switching all settings sections", checks);
            Check(LayoutState().Equals(homeState) && ReferenceEquals(homePreview, ViewModel.PreviewBytes),
                "Return home preserves arrangement/export settings and exact preview instance", checks);
            page.RequestBack();
            Check(returnEvents == 1 && SettingsFrame.BackStack.Count == 0 && SettingsFrame.ForwardStack.Count == 0,
                "Duplicate back is ignored and only settings frame history is cleared", checks);
            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                OpenSettings(SettingsSection.General); page = (SettingsPage)SettingsFrame.Content!;
                page.NavigateTo(SettingsSection.Export); page.NavigateTo(SettingsSection.Layout); page.NavigateTo(section);
                await Task.Delay(150);
                InvokeButton((Button)page.FindName("SettingsBackButton")); await Task.Delay(50);
                Check(WorkspaceGrid.Visibility == Visibility.Visible && SettingsFrame.Content is null &&
                      ViewModel.Camera == "Keep manual camera" && ViewModel.SourcePath == fixture && LayoutState().Equals(homeState),
                    $"{section}: one back click returns existing work home regardless of section history", checks);
            }
            Check(ViewModel.SourcePath == fixture && ViewModel.Camera == "Keep manual camera", "Settings theme return preserves photo/manual data", checks);
            await VerifyMainLayoutAsync(checks, "1344x892-light");
            await CaptureWorkspaceAsync("figma-main-light.png");
            ViewModel.ThemeChoice = 1;

            // Use a scratch fixture: corrupt it temporarily and prove failed Reload is transactional.
            var originalBytes = await File.ReadAllBytesAsync(fixture);
            await File.WriteAllBytesAsync(fixture, [1, 2, 3]);
            Check(!await ViewModel.ReloadExifAsync(() => Task.FromResult(true)) && ViewModel.Camera == "Keep manual camera", "Failed Reload preserves all fields", checks);
            await File.WriteAllBytesAsync(fixture, originalBytes);
            Check(await ViewModel.ReloadExifAsync(() => Task.FromResult(true)), "Reload recovers after source becomes readable", checks);
            await WaitForPreviewAsync();
            ViewModel.ShutterInput = "invalid";
            Check(ViewModel.InputError.Length > 0 && !ViewModel.CanExport, "Invalid shutter prevents export", checks);
            ViewModel.ShutterInput = "0.8"; await WaitForPreviewAsync();
            Check(ViewModel.Shutter == "0.8s" && ViewModel.CanExport, "Decimal shutter accepted", checks);

            await ViewModel.LoadPhotoAsync(sample); await WaitForPreviewAsync();
            var creation = File.GetCreationTime(sample);
            Check(ViewModel.SelectedDate?.Date == creation.Date && ViewModel.CaptureTimeSource == "檔案建立日期" &&
                  double.IsNaN(ViewModel.FocalLengthValue) && ViewModel.Camera.Length == 0, "No EXIF: no fake metadata; creation-date fallback", checks);
            Check(await ViewModel.ReloadExifAsync() && ViewModel.PreviewCaption.Contains("無 EXIF", StringComparison.Ordinal) && ViewModel.StatusText.Contains("建立日期", StringComparison.Ordinal), "No-EXIF Reload gives short notice", checks);
            await WaitForPreviewAsync();
            ViewModel.Camera = "Panasonic DC-GH5"; ViewModel.Lens = "LEICA DG 12-60mm F2.8-4.0";
            ViewModel.FocalLengthValue = 35; ViewModel.ApertureValue = 2.8; ViewModel.ShutterInput = "1/250"; ViewModel.IsoValue = 200;
            await WaitForPreviewAsync();

            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var nativeWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var sizing = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMax>());
            try
            {
                Marshal.StructureToPtr(new NativeMinMax(), sizing, false);
                SendMessage(nativeWindow, 0x0024, IntPtr.Zero, sizing);
                var minimum = Marshal.PtrToStructure<NativeMinMax>(sizing);
                Check(minimum.MinTrack.X >= 576 * GetDpiForWindow(nativeWindow) / 96d,
                    "Native minimum window width scales with DPI", checks);
            }
            finally { Marshal.FreeHGlobal(sizing); }
            var scale = GetDpiForWindow(nativeWindow) / 96d;
            Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Resize(new Windows.Graphics.SizeInt32((int)Math.Ceiling(576 * scale), (int)Math.Ceiling(420 * scale)));
            await Task.Delay(150);
            await VerifyMainLayoutAsync(checks, "minimum-window");
            await CaptureWorkspaceAsync("figma-main-minimum.png");
            foreach (var size in new[] { new Windows.Graphics.SizeInt32(1280, 820), new Windows.Graphics.SizeInt32(1024, 768) })
            {
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id).Resize(size); await Task.Delay(100);
                await VerifyMainLayoutAsync(checks, $"{size.Width}x{size.Height}");
                await CaptureWorkspaceAsync($"figma-main-{size.Width}.png");
            }
            Settings_Click(SettingsButton, new RoutedEventArgs()); page = (SettingsPage)SettingsFrame.Content!;
            await Task.Delay(250);
            navigation = (NavigationView)page.FindName("SettingsNavigation");
            Check(navigation.DisplayMode != NavigationViewDisplayMode.Expanded && navigation.IsBackButtonVisible == NavigationViewBackButtonVisible.Visible,
                "Narrow settings retain native compact navigation/back", checks);
            await CaptureWorkspaceAsync("figma-settings-compact.png");
            var nativeBack = Descendants(navigation).OfType<Button>().First(button => button.Name == "NavigationViewBackButton");
            page.NavigateTo(SettingsSection.Layout); page.NavigateTo(SettingsSection.Camera); page.NavigateTo(SettingsSection.Export);
            InvokeButton(nativeBack); await Task.Delay(100);
            Check(WorkspaceGrid.Visibility == Visibility.Visible && SettingsFrame.Content is null && ViewModel.HasPhoto,
                "Native compact NavigationView back also returns directly home", checks);
            SettingsScrollViewer.ChangeView(null, SettingsScrollViewer.ScrollableHeight, null, true); await Task.Delay(100);
            Check(PhotoTimePicker.TransformToVisual(SettingsScrollViewer).TransformPoint(new Point()).Y < SettingsScrollViewer.ActualHeight, "Photo date/time reachable by vertical scrolling", checks);
            await CaptureWorkspaceAsync("figma-main-metadata-small.png");

            // Real export, prove guide rendering is separate and source is untouched.
            var export = Path.Combine(AppContext.BaseDirectory, "figma-export.jpg");
            var hashBefore = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sample));
            await ViewModel.ExportAsync(export);
            using (var image = new MagickImage(export))
                Check(image.Width == 1080 && image.Height == 1080 && image.GetExifProfile() is null, "Actual square JPG export without original EXIF", checks);
            var firstExport = await File.ReadAllBytesAsync(export);
            ViewModel.ShowGuides = !ViewModel.ShowGuides; await WaitForPreviewAsync(); await ViewModel.ExportAsync(export);
            var secondExport = await File.ReadAllBytesAsync(export);
            var sourceAfter = await File.ReadAllBytesAsync(sample);
            Check(firstExport.SequenceEqual(secondExport), "Preview guides never enter exported JPG", checks);
            Check(hashBefore.SequenceEqual(System.Security.Cryptography.SHA256.HashData(sourceAfter)), "Source image unchanged", checks);
            await ViewModel.FlushSettingsAsync();
            var restored = await new SettingsStore(Path.Combine(AppContext.BaseDirectory, "ui-figma-regression-settings.json")).LoadAsync();
            Check(restored.MarginRules.Any(item => item.Id == rule.Id) && restored.SelectedMarginRuleId == rule.Id && restored.Layout.LensFontSize == 24, "Rules, independent fonts and selection persisted", checks);
            Check(restored.Layout.Arrangement == InformationArrangement.Stacked, "Selected arrangement is persisted with existing preferences", checks);
            using var restarted = CreateRegressionViewModel(Path.Combine(AppContext.BaseDirectory, "ui-figma-regression-settings.json"));
            await restarted.InitializeAsync();
            Check(restarted.Arrangement == InformationArrangement.Stacked && restarted.SelectedMarginRuleId == rule.Id && restarted.JpegQuality == ViewModel.JpegQuality,
                "Fresh application ViewModel restores arrangement and other preferences", checks);
            var migrationPath = Path.Combine(AppContext.BaseDirectory, "ui-v2-migration-settings.json");
            await File.WriteAllTextAsync(migrationPath, """{"version":2,"layout":{"photoSideMargin":61,"primaryFontSize":30},"theme":2}""");
            using var migrated = new PhotoMargin.App.ViewModels.MainViewModel(new PhotoMargin.Imaging.ImageLoader(new PhotoMargin.Imaging.ExifReader()),
                new PhotoMargin.Core.LayoutEngine(new PhotoMargin.Imaging.MagickTextMeasurer()), new PreviewService(new PhotoMargin.Imaging.ImageRenderer()),
                new PhotoMargin.Imaging.ExportService(new PhotoMargin.Imaging.ImageRenderer()), new SettingsStore(migrationPath));
            await migrated.InitializeAsync();
            Check(migrated.PhotoSideMargin == 61 && migrated.PrimaryFontSize == 30 && migrated.ThemeChoice == 2 && migrated.MarginRules.Count == 1,
                "Version 2 settings migrate existing margins into a rule", checks);
            Check(migrated.Arrangement == InformationArrangement.Auto, "Old version 2 without arrangement uses legacy auto", checks);
            var version3Path = Path.Combine(AppContext.BaseDirectory, "ui-v3-arrangement-migration.json");
            await File.WriteAllTextAsync(version3Path, """{"version":3,"layout":{"outputSize":1536,"jpegQuality":87},"theme":1}""");
            using var legacy3 = CreateRegressionViewModel(version3Path); await legacy3.InitializeAsync();
            Check(legacy3.Arrangement == InformationArrangement.Auto && legacy3.OutputSize == 1536 && legacy3.JpegQuality == 87,
                "Old version 3 without arrangement preserves preferences and selects legacy auto", checks);
        }
        catch (Exception exception) { failure = exception; await CaptureWorkspaceAsync("figma-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-regression-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
    private object LayoutState() => (ViewModel.SelectedMarginRuleId, ViewModel.PhotoSideMargin, ViewModel.PhotoTopMargin,
        ViewModel.PhotoInfoGap, ViewModel.BottomSafeDistance, ViewModel.CropExtraPadding, ViewModel.FontFamily,
        ViewModel.PrimaryFontSize, ViewModel.LensFontSize, ViewModel.SecondaryFontSize, ViewModel.DateFontSize,
        ViewModel.OutputSize, ViewModel.JpegQuality, ViewModel.ShowGuides, ViewModel.Arrangement);
    private static ViewModels.MainViewModel CreateRegressionViewModel(string settingsPath)
    {
        var renderer = new ImageRenderer();
        return new(new ImageLoader(new ExifReader()), new LayoutEngine(new MagickTextMeasurer()),
            new PreviewService(renderer), new ExportService(renderer), new SettingsStore(settingsPath));
    }
    private async Task VerifyArrangementAsync(List<object> checks)
    {
        RootGrid.UpdateLayout();
        var arrangementBounds = ElementBounds(ArrangementBox); var fontBounds = ElementBounds(FontFamilyBox);
        Check(SidebarContent.Children.IndexOf(ArrangementBox) + 1 == SidebarContent.Children.IndexOf(FontFamilyBox) &&
              Math.Abs(arrangementBounds.Left - fontBounds.Left) < 1 && Math.Abs(arrangementBounds.Width - fontBounds.Width) < 1 &&
              Math.Abs(arrangementBounds.Height - fontBounds.Height) < 1 && arrangementBounds.Bottom < fontBounds.Top,
            "Arrangement sits immediately above font selector with identical native dimensions", checks);
        Check(ViewModel.Arrangement == InformationArrangement.Auto && ArrangementBox.SelectedItem is ViewModels.ArrangementChoice { Value: InformationArrangement.Auto } && ArrangementBox.Items.Count == 3,
            "Default native arrangement selector exposes only supported modes", checks);
        var guides = ViewModel.ShowGuides; ViewModel.ShowGuides = false;
        using var source = new ImageLoader(new ExifReader()).Load(ViewModel.SourcePath!);
        var renderer = new ImageRenderer(); var engine = new LayoutEngine(new MagickTextMeasurer());
        byte[]? columnsJpeg = null;
        foreach (var choice in ViewModel.ArrangementChoices)
        {
            ArrangementBox.SelectedItem = choice;
            await WaitForPreviewAsync();
            Check(ViewModel.Arrangement == choice.Value, $"{choice.Name}: native selector updates actual setting", checks);
            var metadata = new DisplayMetadata(ViewModel.Camera, ViewModel.Lens, ViewModel.FocalLength, ViewModel.Aperture, ViewModel.Shutter, ViewModel.Iso,
                ViewModel.SelectedDate!.Value.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture) +
                $" {ViewModel.SelectedTime!.Value.Hours:00}:{ViewModel.SelectedTime.Value.Minutes:00}");
            var layout = engine.Calculate(source.OrientedSize, metadata, new LayoutSettings
            {
                Arrangement = choice.Value, OutputSize = ViewModel.OutputSize, JpegQuality = (int)ViewModel.JpegQuality,
                PrimaryFontSize = ViewModel.PrimaryFontSize, LensFontSize = ViewModel.LensFontSize,
                SecondaryFontSize = ViewModel.SecondaryFontSize, DateFontSize = ViewModel.DateFontSize, FontFamily = ViewModel.FontFamily,
                PhotoSideMargin = ViewModel.PhotoSideMargin, PhotoTopMargin = ViewModel.PhotoTopMargin, PhotoInfoGap = ViewModel.PhotoInfoGap,
                BottomSafeDistance = ViewModel.BottomSafeDistance, CropExtraPadding = ViewModel.CropExtraPadding,
                ProtectFourByFive = ViewModel.ProtectFourByFive, ProtectFiveByFour = ViewModel.ProtectFiveByFour,
                Visibility = new FieldVisibility { Camera = true, Lens = true, Exposure = true, Date = true }
            });
            Check(layout.IsValid && layout.UsesStackedInfo == (choice.Value == InformationArrangement.Stacked),
                $"{choice.Name}: actual supported layout remains safe", checks);
            using (var preview = new MagickImage(ViewModel.PreviewBytes!))
            using (var expected = new MagickImage(renderer.RenderPng(source, layout)))
                Check(preview.Compare(expected, ErrorMetric.Absolute) == 0, $"{choice.Name}: live preview matches selected core layout pixel-for-pixel", checks);
            var export = Path.Combine(AppContext.BaseDirectory, $"arrangement-{choice.Value}.jpg");
            await ViewModel.ExportAsync(export);
            var jpeg = await File.ReadAllBytesAsync(export);
            Check(jpeg.SequenceEqual(renderer.RenderJpeg(source, layout, (int)ViewModel.JpegQuality)),
                $"{choice.Name}: real export uses same selected layout as preview", checks);
            if (choice.Value == InformationArrangement.TwoColumns) columnsJpeg = jpeg;
            if (choice.Value == InformationArrangement.Stacked)
                Check(columnsJpeg is not null && !columnsJpeg.SequenceEqual(jpeg), "Switching arrangement changes actual rendered JPG", checks);
            SettingsScrollViewer.ChangeView(null, 300, null, true); await Task.Delay(150);
            await CaptureWindowAsync($"arrangement-{choice.Value}.png");
        }
        ViewModel.ShowGuides = guides; await WaitForPreviewAsync();
        SettingsScrollViewer.ChangeView(null, 0, null, true); await Task.Delay(50);
    }
    private async Task WaitForPreviewAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (ViewModel.CanExport && PreviewImage.Source is not null) { await Task.Delay(100); RootGrid.UpdateLayout(); return; }
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"Preview did not complete: {ViewModel.ErrorText} {ViewModel.InputError}");
    }
    private async Task VerifyMainLayoutAsync(List<object> checks, string state)
    {
        RootGrid.UpdateLayout();
        var scrollbar = Descendants(SettingsScrollViewer).OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical && bar.ActualWidth > 0);
        scrollbar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
        VisualStateManager.GoToState(scrollbar, "ExpandedWithoutAnimation", false);
        await Task.Delay(500); RootGrid.UpdateLayout();
        var viewport = ElementBounds(SettingsScrollViewer); var actions = ElementBounds(PhotoActions); var canvas = ElementBounds(PreviewCanvas);
        Check(viewport.Right <= RootGrid.ActualWidth + 1 && viewport.Bottom <= actions.Top, $"{state}: sidebar/actions separated and inside window", checks);
        Check(Math.Abs(canvas.Width - canvas.Height) < 1, $"{state}: square preview", checks);
        Check(SettingsScrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed && SettingsScrollViewer.ScrollableHeight > 0, $"{state}: vertical-only scrolling", checks);
        var left = ElementBounds(scrollbar).Left;
        foreach (var control in new FrameworkElement[] { MarginRuleBox, AddRuleButton, BrandImageBox, ArrangementBox, FontFamilyBox, CameraTextBox, LensTextBox, JpegQualitySlider, IsoBox })
            Check(ElementBounds(control).Right <= left - 1, $"{state}: {control.Name} clears expanded scrollbar", checks);
        var minute = Descendants(PhotoTimePicker).OfType<TextBlock>().Single(element => element.Name == "MinuteTextBlock");
        var minuteHost = Descendants(PhotoTimePicker).OfType<Border>().Single(element => element.Name == "SecondPickerHost");
        var timeButton = Descendants(PhotoTimePicker).OfType<Button>().Single(element => element.Name == "FlyoutButton");
        Check(minute.ActualWidth >= 12 && minuteHost.ActualWidth >= 40 && ElementBounds(timeButton).Right <= ElementBounds(PhotoTimePicker).Right + 1,
            $"{state}: both hour and minute remain visible (minute cell {minuteHost.ActualWidth:0.0}, button {timeButton.ActualWidth:0.0}, picker {PhotoTimePicker.ActualWidth:0.0})", checks);
    }
    private Rect ElementBounds(FrameworkElement element) => element.TransformToVisual(RootGrid).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        { var child = VisualTreeHelper.GetChild(parent, index); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Check(bool passed, string name, List<object> checks)
    { if (!passed) throw new InvalidOperationException(name); checks.Add(new { Name = name, Passed = true }); }
    private static void InvokeButton(Button button) =>
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
    private async Task CaptureWorkspaceAsync(string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(RootGrid); var pixels = await bitmap.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(pixels); var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        await WritePngAsync(name, bytes, bitmap.PixelWidth, bitmap.PixelHeight);
    }
    private async Task WritePngAsync(string name, byte[] bytes, int width, int height)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(AppContext.BaseDirectory);
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private async Task CaptureWindowAsync(string name)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        GetWindowRect(hwnd, out var rect); var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var dc = GetWindowDC(hwnd); var memory = CreateCompatibleDC(dc);
        var info = new BitmapInfo { Header = new BitmapHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 } };
        var bitmap = CreateDIBSection(dc, ref info, 0, out var pixels, IntPtr.Zero, 0); var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(hwnd, memory, 2)) return;
            var bytes = new byte[width * height * 4]; Marshal.Copy(pixels, bytes, 0, bytes.Length);
            for (var index = 3; index < bytes.Length; index += 4) bytes[index] = 255;
            await WritePngAsync(name, bytes, width, height);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(hwnd, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMax { public NativePoint Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
