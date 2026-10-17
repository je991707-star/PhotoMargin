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
    internal async Task RunBuiltInCatalogUpdateRegressionAsync(bool restore)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await UntilPresetAsync(() => RootGrid.IsLoaded); await Task.Delay(200);
            var canonical = BuiltInMarginPresets.Create();
            Check(ViewModel.MarginRules.Count == 2 && ViewModel.MarginRules.Single(rule => rule.Id == MarginRule.DefaultId) == canonical,
                "Installed schema 8/revision 1 upgrades to the real eight-entry table", checks);
            var backup = ViewModel.MarginRules.Single(rule => rule.Id != MarginRule.DefaultId);
            Check(Enum.GetValues<SourceAspect>().All(aspect => new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked }
                .All(arrangement => backup.ForAspect(aspect, arrangement) == new MarginConfiguration { Top = 100, Gap = 50, Bottom = 50 })),
                "All eight previous configurations survive in one independent custom backup", checks);
            Check(ViewModel.LensFontSize == 18, "Previously saved lens font is not reset", checks);
            if (!restore) Check(ViewModel.BottomSafeDistance == 112, "Unmodified current work follows the upgraded 4:3 configuration", checks);
            OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content; await WaitForDemoAsync(page);
            for (var a = 0; a < 4; a++) for (var b = 0; b < 2; b++)
            {
                ((ComboBox)page.FindName("ImageAspectSelector")).SelectedIndex = a;
                ((ComboBox)page.FindName("InformationArrangementSelector")).SelectedIndex = b; await WaitForDemoAsync(page);
                var expected = canonical.ForAspect((SourceAspect)a, b == 1 ? InformationArrangement.Stacked : InformationArrangement.TwoColumns);
                var editor = ViewModel.RuleEditor;
                Check(new[] { editor.Side, editor.Top, editor.Bottom, editor.Gap, editor.ExposureItemGap, editor.Extra }
                    .SequenceEqual(new[] { expected.Side, expected.Top, expected.Bottom, expected.Gap, expected.ExposureItemGap, expected.Extra }),
                    $"Repaired native ratio {a}/arrangement {b} uses all six canonical values", checks);
            }
            await CaptureWindowAsync(restore ? "builtin-update-restored.png" : "builtin-update.png");
            await page.RequestBackAsync();
            await ViewModel.FlushSettingsAsync();
            var saved = await new SettingsStore(Path.Combine(AppContext.BaseDirectory, "ui-built-in-update-settings.json")).LoadAsync();
            Check(saved.MarginRules.Count == 2 && saved.MarginRules.Single(rule => rule.Id == backup.Id) == backup,
                "Save/reload keeps the backup ID and does not duplicate or reset the table", checks);
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("builtin-update-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, restore ? "builtin-update-restore-result.json" : "builtin-update-result.json"),
                JsonSerializer.Serialize(new { Passed = failure is null, Checks = checks.Count,
                    Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                    new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }

    internal async Task RunBuiltInLensRegressionAsync(bool restore)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; await UntilPresetAsync(() => RootGrid.IsLoaded);
            await Task.Delay(200); RootGrid.UpdateLayout(); ViewModel.ThemeChoice = 1;
            var builtin = BuiltInMarginPresets.Create();
            Check(ViewModel.MarginRules.Count == 1 && ViewModel.MarginRules[0] == builtin,
                "One canonical built-in preset, eight configurations, no duplicates", checks);
            if (restore)
            {
                Check(ViewModel.LensFontSize == 12 && LensFontSizeBox.SelectedItem is double value && value == 12,
                    "Fresh process restores 12px in the real editable lens ComboBox", checks);
                Check(!ViewModel.HasPhoto && ViewModel.LensFontSizes.Min() == 8 && ViewModel.FontSizes.Min() == 18,
                    "Restart restores preferences without inventing a source or lowering other font limits", checks);
                await CaptureWindowAsync("builtin-lens-restored.png"); return;
            }
            Check(ViewModel.LensFontSize == 16 && LensFontSizeBox.Items.Cast<double>().Min() == 8 &&
                LensFontSizeBox.Items.Cast<double>().Max() == 100 && !CameraFontSizeBox.Items.Cast<double>().Contains(8),
                "Native lens defaults to 16 with 8–100 options; camera font options still start at 18", checks);
            AppWindow.Resize(new SizeInt32(1596, 1256)); OpenSettings(SettingsSection.Layout);
            var page = (SettingsPage)SettingsFrame.Content; await WaitForDemoAsync(page);
            var expected = new[]
            {
                new[] {40d,100,170,50,18,24}, new[] {40d,100,150,50,20,24},
                new[] {48d,50,90,50,18,24}, new[] {48d,50,70,50,20,24},
                new[] {48d,100,112,50,18,24}, new[] {48d,100,100,32,20,24},
                new[] {48d,100,136,50,18,24}, new[] {48d,100,106,50,18,24}
            };
            for (var a = 0; a < 4; a++) for (var b = 0; b < 2; b++)
            {
                ((ComboBox)page.FindName("ImageAspectSelector")).SelectedIndex = a;
                ((ComboBox)page.FindName("InformationArrangementSelector")).SelectedIndex = b; await WaitForDemoAsync(page);
                var editor = ViewModel.RuleEditor;
                Check(new[] { editor.Side, editor.Top, editor.Bottom, editor.Gap, editor.ExposureItemGap, editor.Extra }.SequenceEqual(expected[a * 2 + b]),
                    $"Native ratio {a}/arrangement {b}: all six fields match requested built-in table", checks);
                Check(editor.PreviewLayout!.IsValid && editor.PreviewLayout.TextRuns.Any(run =>
                    run.Text.Contains("LEICA", StringComparison.Ordinal) && Math.Abs(run.FontSize - 16 * 720d / 1080) < 0.001),
                    $"Native ratio {a}/arrangement {b}: independent demo renders the default 16px lens size", checks);
            }
            await CaptureWindowAsync("builtin-eight-table.png"); await page.RequestBackAsync();
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "PhotoMargin.sln"))) repository = repository.Parent;
            var source = Path.Combine(repository!.FullName, "samples", "input", "sample_3x2.png");
            await ViewModel.LoadPhotoAsync(source); ViewModel.ShowGuides = false; ViewModel.Arrangement = InformationArrangement.Stacked;
            const string lens = "Panasonic Lumix G Vario 45-200mm f/4-5.6 MEGA O.I.S.";
            ViewModel.Lens = lens; ViewModel.FontFamily = "Consolas"; await WaitForPreviewAsync();
            ApplyLensFontSizeText(LensFontSizeBox, "12.5"); await WaitForPreviewAsync();
            Check(ViewModel.LensFontSize == 12.5 && LensFontSizeBox.SelectedItem is double fraction && fraction == 12.5,
                "Submitted fractional lens text stays selected and equals the actual model value", checks);
            ApplyLensFontSizeText(LensFontSizeBox, "invalid");
            Check(ViewModel.LensFontSize == 12.5 && LensFontSizeBox.Text == "12.5", "Invalid submitted text restores the previous valid selection", checks);
            using var photo = new ImageLoader(new ExifReader()).Load(source);
            var renderer = new ImageRenderer(); var engine = new LayoutEngine(new MagickTextMeasurer());
            foreach (var size in new[] { 8d, 12, 16, 18 })
            {
                LensFontSizeBox.SelectedItem = size;
                foreach (var output in new[] { 1080, 2048 })
                {
                    ViewModel.OutputSize = output; await WaitForPreviewAsync();
                    Check(ViewModel.LensFontSize == size && ViewModel.Lens == lens, $"{output}/{size}: native selection keeps requested lens size and entire original name", checks);
                    var metadata = new DisplayMetadata(ViewModel.Camera, ViewModel.Lens, ViewModel.FocalLength, ViewModel.Aperture, ViewModel.Shutter, ViewModel.Iso,
                        ViewModel.SelectedDate!.Value.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture) +
                        $" {ViewModel.SelectedTime!.Value.Hours:00}:{ViewModel.SelectedTime.Value.Minutes:00}");
                    var settings = MarginConfiguration.From(ViewModel.WorkingMargins).ApplyTo(new()
                    {
                        OutputSize = output, FontFamily = ViewModel.FontFamily, Arrangement = ViewModel.Arrangement, LensFontSize = size,
                        PrimaryFontSize = ViewModel.PrimaryFontSize, SecondaryFontSize = ViewModel.SecondaryFontSize, DateFontSize = ViewModel.DateFontSize,
                        Visibility = new() { Date = true }
                    });
                    var layout = engine.Calculate(photo.OrientedSize, metadata, settings);
                    Check(layout.IsValid && layout.TextRuns.Any(run => run.Text == lens && Math.Abs(run.FontSize - size * output / 1080) < 0.001),
                        $"{output}/{size}: measured glyph runs use exact selected size, with no automatic shrinking", checks);
                    using var expectedPreview = new MagickImage(await renderer.RenderPreviewAsync(photo, layout, false));
                    using var actualPreview = new MagickImage(ViewModel.PreviewBytes!);
                    Check(expectedPreview.Compare(actualPreview, ErrorMetric.Absolute) == 0, $"{output}/{size}: main preview uses the same complete layout", checks);
                    var filename = Path.Combine(AppContext.BaseDirectory, $"lens-font-{size:0}-{output}.jpg"); await ViewModel.ExportAsync(filename);
                    using var expectedExport = new MagickImage(renderer.RenderJpeg(photo, layout, (int)ViewModel.JpegQuality));
                    using var actualExport = new MagickImage(filename);
                    Check(expectedExport.Compare(actualExport, ErrorMetric.Absolute) == 0, $"{output}/{size}: actual exported JPG matches measured preview geometry", checks);
                    await File.WriteAllBytesAsync(Path.ChangeExtension(filename, ".png"), ViewModel.PreviewBytes!);
                }
                ViewModel.OutputSize = 1080; await WaitForPreviewAsync();
                SettingsScrollViewer.ChangeView(null, 240, null, true); await Task.Delay(100);
                await CaptureWindowAsync($"builtin-lens-{size:0}.png");
            }
            ApplyLensFontSizeText(LensFontSizeBox, "12"); await WaitForPreviewAsync(); await ViewModel.FlushSettingsAsync();
            Check((await new SettingsStore(Path.Combine(AppContext.BaseDirectory, "ui-built-in-lens-settings.json")).LoadAsync()).Layout.LensFontSize == 12,
                "Actual preferences persist 12px instead of clamping to 18", checks);
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("builtin-lens-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, restore ? "builtin-lens-restore-result.json" : "builtin-lens-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
