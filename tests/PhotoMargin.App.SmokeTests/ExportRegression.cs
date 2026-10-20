using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Views;
using PhotoMargin.Core.Models;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunExportRegressionAsync(bool restore)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await UntilPresetAsync(() => RootGrid.IsLoaded); ViewModel.ThemeChoice = 1;
            AppWindow.Resize(new SizeInt32(1344, 892)); await Task.Delay(250);
            if (restore)
            {
                Check(ViewModel.ExportFormatIndex == 2 && ViewModel.WebpLossless && ViewModel.ExportQuality == 37 && ViewModel.JpegQuality == 87 && ViewModel.OutputSize == 1440,
                    "Fresh process restores independent formats, qualities, lossless and output size", checks);
                Check(ViewModel.FileNamePattern == "{name}_{date}" && ViewModel.RememberExportLocation && ViewModel.IncludeExportMetadata, "Storage and metadata preferences restored", checks);
            }
            Check(MainExportFormatBox.IsEnabled && OutputSizeBox.IsEnabled && !ExportButton.IsEnabled && !SidebarSettingsHost.IsEnabled,
                "No photo permits export preferences, not export or photo editing", checks);
            await CaptureWindowAsync("export-main-empty.png");
            OpenSettings(SettingsSection.Export); var page = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            var format = (ComboBox)page.FindName("SettingsExportFormatBox");
            format.SelectedIndex = 1; await Task.Delay(150);
            Check(ViewModel.ExportFormatIndex == 1 && MainExportFormatBox.SelectedIndex == 1 && JpegQualitySlider.Visibility == Visibility.Collapsed &&
                ((Slider)page.FindName("SettingsExportQualitySlider")).Visibility == Visibility.Collapsed, "PNG hides percentage in both views", checks);
            await CaptureWindowAsync("export-settings-png.png");
            format.SelectedIndex = 2; ViewModel.WebpLossless = false; ViewModel.ExportQuality = 37;
            ViewModel.ExportSizeIndex = 1; await Task.Delay(200);
            Check(ViewModel.OutputSize == 1440 && JpegQualitySlider.Value == 37 && ((ToggleSwitch)page.FindName("WebpLosslessSwitch")).Visibility == Visibility.Visible,
                "Settings WebP quality and size bind to main", checks);
            await CaptureWindowAsync("export-settings-webp.png");
            ViewModel.WebpLossless = true; await Task.Delay(100);
            Check(JpegQualitySlider.Visibility == Visibility.Collapsed && !ViewModel.ShowExportQuality, "WebP lossless hides percentage", checks);
            ViewModel.FileNameMode = 1; ViewModel.FileNamePattern = "{name}_{date}";
            var scroll = (ScrollViewer)page.FindName("CommonSettingsScrollViewer"); await Task.Delay(100); scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Task.Delay(150);
            Check(scroll.ScrollableHeight > 0 && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Collapsed, "Export page vertically scrolls without horizontal scrollbar", checks);
            await CaptureWindowAsync("export-settings-storage.png");
            AppWindow.Resize(new SizeInt32(1100, 760)); await Task.Delay(200); scroll.ChangeView(null, 0, null, true); await Task.Delay(100);
            Check(format.ActualWidth > 200 && ElementBounds(format).Right < RootGrid.ActualWidth, "Small-window export format fits", checks);
            await CaptureWindowAsync("export-settings-small.png");
            ViewModel.ThemeChoice = 2; await Task.Delay(150); await CaptureWindowAsync("export-settings-light.png"); ViewModel.ThemeChoice = 1;
            await page.RequestBackAsync();
            var source = Environment.GetEnvironmentVariable("PHOTOMARGIN_EDITED_SAMPLE") ?? throw new InvalidOperationException("Missing JPEG fixture");
            await ViewModel.LoadPhotoAsync(source); ViewModel.Arrangement = InformationArrangement.Stacked; ViewModel.OutputSize = 1080; ViewModel.ShowGuides = true;
            await WaitForPreviewAsync(); AppWindow.Resize(new SizeInt32(1344, 892)); await Task.Delay(200); SettingsScrollViewer.ChangeView(null, 0, null, true);
            ViewModel.ExportFormatIndex = 0; ViewModel.JpegQuality = 87; await Task.Delay(180); await CaptureWindowAsync("export-main-loaded.png");
            var original = ViewModel.SourcePath;
            foreach (var index in new[] { 0, 1, 2 })
            {
                ViewModel.ExportFormatIndex = index; ViewModel.IncludeExportMetadata = true;
                var extension = new ExportPreferences { Format = (ExportFormat)index }.Extension;
                var path = Path.Combine(AppContext.BaseDirectory, "export-actual-" + index + extension);
                ViewModel.ConflictMode = 2; await ViewModel.ExportAsync(path);
                using var decoded = new MagickImage(path);
                Check(decoded.Width == 1080 && decoded.Height == 1080 && decoded.GetColorProfile() is not null && decoded.GetExifProfile() is not null,
                    $"Real photo export {index} has pixels, metadata and ICC", checks);
                Check(ViewModel.StatusText.StartsWith("已匯出") && ViewModel.SourcePath == original, "Completion and original source preserved", checks);
            }
            ViewModel.ExportFormatIndex = 2; ViewModel.WebpLossless = true; ViewModel.ExportQuality = 37; ViewModel.OutputSize = 1440; ViewModel.ConflictMode = 0;
            await WaitForPreviewAsync(); await ViewModel.FlushSettingsAsync();
        }
        catch (Exception e) { failure = e; await CaptureWindowAsync("export-ui-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, restore ? "export-restore-result.json" : "export-regression-result.json"),
                JsonSerializer.Serialize(new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
