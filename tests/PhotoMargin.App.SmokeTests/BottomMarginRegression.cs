using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.App.Views;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunBottomMarginRegressionAsync()
    {
        var checks = new List<object>(); var measurements = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; ViewModel.ThemeChoice = 1;
            ViewModel.FontFamily = "Consolas"; ViewModel.PrimaryFontSize = 26;
            ViewModel.LensFontSize = 20; ViewModel.SecondaryFontSize = 20; ViewModel.DateFontSize = 20;
            AppWindow.Resize(new SizeInt32(1596, 1256)); OpenSettings(SettingsSection.Layout);
            var page = (SettingsPage)SettingsFrame.Content; await WaitForDemoAsync(page);
            var names = new[] { ("BottomMarginSlider", "下方邊距"), ("InformationGapSlider", "照片與文字間隔"),
                ("ExposureItemGapSlider", "焦距、曝光三要素間隔"), ("ExtraPaddingSlider", "文字與左右邊界") };
            foreach (var (id, label) in names)
                Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName((Slider)page.FindName(id)) == label + "（1080 基準 px）",
                    $"Native slider uses updated label: {label}", checks);
            Check(Descendants((FrameworkElement)page.FindName("LayoutContent")).OfType<Slider>().Count() == 6,
                "Exactly six existing sliders, no extra control entrances", checks);
            Check(!ViewModel.ProtectFiveByFour && !ViewModel.MarginRules[0].ProtectLandscape,
                "Default work and preset use portrait 4:5 only", checks);
            await ViewModel.RuleEditor.ChangeModeAsync(ViewModels.PresetMode.Edit);
            var editor = ViewModel.RuleEditor; editor.AspectIndex = 0;
            foreach (var index in new[] { 0, 1 })
            {
                ((ComboBox)page.FindName("InformationArrangementSelector")).SelectedIndex = index;
                editor.Top = 100; editor.Extra = 0; editor.Gap = 39; editor.ExposureItemGap = 16;
                foreach (var bottom in new[] { 0, 144 })
                {
                    ((Slider)page.FindName("BottomMarginSlider")).Value = bottom; await WaitForDemoAsync(page);
                    var layout = editor.PreviewLayout!;
                    Check(Math.Abs(layout.TextRuns.Max(run => run.Bounds.Bottom) - (720 - bottom * 720d / 1080)) < 0.001,
                        $"Native {index}/{bottom}: measured text bottom follows slider immediately", checks);
                    Check(Math.Abs(layout.TextRuns.Min(run => run.Bounds.Top) - layout.PhotoBounds.Bottom - 39 * 720d / 1080) < 0.001,
                        $"Native {index}/{bottom}: photo-to-text gap remains independent", checks);
                    await CaptureWindowAsync($"bottom-settings-{index}-{bottom}.png");
                }
                var originalBottom = editor.PreviewLayout!.TextRuns.Max(run => run.Bounds.Bottom);
                editor.Extra = 80; await WaitForDemoAsync(page);
                Check(Math.Abs(originalBottom - editor.PreviewLayout!.TextRuns.Max(run => run.Bounds.Bottom)) < 0.001,
                    $"Native {index}: horizontal inset never moves text bottom", checks);
            }
            Check(!ViewModel.HasPhoto && string.IsNullOrEmpty(ViewModel.Camera) && ViewModel.MarginRules[0].Bottom == BuiltInMarginPresets.Create().Bottom,
                "Editing preview does not overwrite preset, main source or actual EXIF", checks);

            using var demo = LayoutPreviewSource.Create(null);
            using var photo = LayoutPreviewSource.Crop(demo, SourceAspect.SixteenByNine);
            var renderer = new ImageRenderer(); var engine = new LayoutEngine(new MagickTextMeasurer());
            foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
            foreach (var size in new[] { 1080, 2048 })
            foreach (var bottom in new[] { 0, 144 })
            {
                var settings = new LayoutSettings
                {
                    OutputSize = size, Arrangement = arrangement, BottomSafeDistance = bottom,
                    PhotoTopMargin = 100, PhotoInfoGap = 39, CropExtraPadding = 0, ExposureItemGap = 16,
                    FontFamily = "Consolas", PrimaryFontSize = 26, LensFontSize = 20, SecondaryFontSize = 20, DateFontSize = 20,
                    Visibility = new() { Date = true }
                };
                var layout = engine.Calculate(photo.OrientedSize, LayoutPreviewSource.DemoMetadata, settings);
                var tag = arrangement == InformationArrangement.TwoColumns ? "horizontal" : "vertical";
                Check(layout.IsValid && !layout.ProtectFiveByFour, $"{tag}/{size}/{bottom}: safe complete composition without hidden top/bottom crop", checks);
                var bytes = renderer.RenderPng(photo, layout);
                var filename = $"bottom-margin-{tag}-{bottom}-{size}.png";
                await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, filename), bytes);
                await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(filename, ".jpg")), renderer.RenderJpeg(photo, layout, 95));
                using var image = new MagickImage(bytes); var ink = image.BoundingBox!;
                var actualMargin = size - (ink.Y + ink.Height);
                Check(actualMargin >= bottom * size / 1080d && actualMargin <= bottom * size / 1080d + 3,
                    $"{tag}/{size}/{bottom}: raster bottom margin = {actualMargin}px (only anti-clipping guard)", checks);
                using var preview = new MagickImage(await renderer.RenderPreviewAsync(photo, layout, false));
                Check(image.Compare(preview, ErrorMetric.Absolute) == 0,
                    $"{tag}/{size}/{bottom}: preview and PNG export pixels match", checks);
                measurements.Add(new { Arrangement = tag, OutputSize = size, RequestedBottomAt1080 = bottom,
                    ActualVisibleInkBottomMargin = actualMargin, ActualPhotoTextGap = layout.TextRuns.Min(run => run.Bounds.Top) - layout.PhotoBounds.Bottom,
                    PhotoAspect = layout.PhotoBounds.Width / layout.PhotoBounds.Height, File = filename });
            }
            await editor.LeaveAsync(() => Task.FromResult(true)); await page.RequestBackAsync();
            Check(WorkspaceGrid.Visibility == Visibility.Visible && !ViewModel.HasPhoto, "Back returns home and discards only the independent editing draft", checks);
            await ViewModel.FlushSettingsAsync();
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("bottom-margin-failure.png"); }
        finally
        {
            await ViewModel.RuleEditor.LeaveAsync(() => Task.FromResult(true));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bottom-margin-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)),
                    Results = checks, Measurements = measurements, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
