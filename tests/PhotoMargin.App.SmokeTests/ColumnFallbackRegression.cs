using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunColumnFallbackAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; AppWindow.Resize(new SizeInt32(1344, 892)); ViewModel.ThemeChoice = 1;
            ViewModel.FontFamily = "Consolas"; ViewModel.PrimaryFontSize = 26;
            ViewModel.LensFontSize = 20; ViewModel.SecondaryFontSize = 20; ViewModel.DateFontSize = 20;
            ViewModel.Arrangement = InformationArrangement.TwoColumns;
            ViewModel.ApplyWorkingMargins(new MarginRule { Side = 26, Top = 59, Gap = 67, Bottom = 47, Extra = 51, ProtectLandscape = false });
            var source = Path.Combine(AppContext.BaseDirectory, "column-fallback-source.jpg");
            using (var image = new MagickImage(MagickColors.SteelBlue, 1600, 1200))
            {
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Make, "Panasonic"); exif.SetValue(ExifTag.Model, "DC-GH5");
                exif.SetValue(ExifTag.LensModel, "Panasonic Lumix G Vario 45-200mm f/4-5.6 MEGA O.I.S.");
                exif.SetValue(ExifTag.DateTimeOriginal, "2026:10:12 22:56:00");
                exif.SetValue(ExifTag.FocalLength, new Rational(78, 1)); exif.SetValue(ExifTag.FNumber, new Rational(10, 1));
                exif.SetValue(ExifTag.ExposureTime, new Rational(1, 125)); exif.SetValue(ExifTag.ISOSpeedRatings, new ushort[] { 100 });
                image.SetProfile(exif); image.Write(source);
            }
            var original = File.ReadAllBytes(source);
            await ViewModel.LoadPhotoAsync(source); await WaitForPreviewAsync();
            Check(ViewModel.HasPhoto && PreviewImage.Source is not null && ViewModel.PreviewBytes is not null,
                "First import with overwide EXIF produces a visible photo instead of a white canvas", checks);
            Check(ViewModel.Arrangement == InformationArrangement.Stacked && ArrangementBox.SelectedIndex == 2,
                "Fallback updates the actual native arrangement selector to centered stack", checks);
            Check(SourceSummaryText.Text.Contains("安全檢查失敗，無法使用左右分欄", StringComparison.Ordinal) &&
                  ViewModel.ArrangementWarning == LayoutEngine.ColumnsFallbackWarning,
                "The existing visible caption explains why columns were rejected", checks);
            Check(ExportButton.IsEnabled && ViewModel.ErrorText.Length == 0,
                "A successfully revalidated centered layout can export despite the recoverable column warning", checks);
            Check(ViewModel.Camera == "Panasonic DC-GH5" && ViewModel.Lens.Contains("45-200mm", StringComparison.Ordinal) && ViewModel.PhotoInfoGap == 67,
                "Fallback does not modify EXIF, margins, gap or font choices", checks);
            SettingsScrollViewer.ChangeView(null, 350, null, true); await Task.Delay(150);
            await CaptureWindowAsync("column-fallback-centered.png");
            ArrangementBox.SelectedIndex = 1;
            await UntilPresetAsync(() => ViewModel.CanExport && ArrangementBox.SelectedIndex == 2);
            Check(ViewModel.ArrangementWarning == LayoutEngine.ColumnsFallbackWarning,
                "Retrying an unsupported native column selection falls back once without a render loop", checks);
            ViewModel.ShowGuides = false; await WaitForPreviewAsync();
            var output = Path.Combine(AppContext.BaseDirectory, "column-fallback-export.jpg");
            await ViewModel.ExportAsync(output);
            using (var image = new MagickImage(output)) Check(image.Width == 1080 && image.Height == 1080, "Centered fallback exports a square JPG", checks);
            Check(original.SequenceEqual(File.ReadAllBytes(source)), "Import and fallback leave the original photograph untouched", checks);
            ArrangementBox.SelectedIndex = 0; await WaitForPreviewAsync();
            Check(ViewModel.ArrangementWarning.Length == 0 && ViewModel.Arrangement == InformationArrangement.Auto,
                "Explicitly choosing Auto clears the previous forced-column warning", checks);
            ArrangementBox.SelectedIndex = 2; await WaitForPreviewAsync();
            Check(ViewModel.ArrangementWarning.Length == 0, "Already centered long text does not report a false column failure", checks);
            ViewModel.Lens = "Short lens"; await WaitForPreviewAsync(); ArrangementBox.SelectedIndex = 1; await WaitForPreviewAsync();
            Check(ArrangementBox.SelectedIndex == 1 && ViewModel.ArrangementWarning.Length == 0, "Fitting columns stay in columns", checks);
            ViewModel.ApplyWorkingMargins(new MarginRule { Top = 400, Bottom = 540, Gap = 200 });
            ViewModel.Lens = new string('長', 100);
            await UntilPresetAsync(() => ViewModel.Arrangement == InformationArrangement.Stacked && ViewModel.ErrorText.Contains("安全檢查失敗", StringComparison.Ordinal));
            Check(!ExportButton.IsEnabled && !ViewModel.CanExport, "If centered content still fails safety, export remains natively disabled", checks);
            ViewModel.ApplyWorkingMargins(new MarginRule()); ViewModel.Lens = "Short lens";
            await WaitForPreviewAsync(); await ViewModel.FlushSettingsAsync();
            var stored = await new SettingsStore(Path.Combine(AppContext.BaseDirectory, "ui-column-fallback-settings.json")).LoadAsync();
            Check(stored.Layout.Arrangement == InformationArrangement.Stacked, "Corrected centered arrangement is persisted", checks);
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("column-fallback-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "column-fallback-result.json"), JsonSerializer.Serialize(new
            { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}
