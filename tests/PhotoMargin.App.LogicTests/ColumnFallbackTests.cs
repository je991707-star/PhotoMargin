using ImageMagick;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    private sealed class PausedMeasurer : ITextMeasurer, IDisposable
    {
        private readonly MagickTextMeasurer inner = new();
        private int armed;
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public void Arm() => Interlocked.Exchange(ref armed, 1);
        public SizeD Measure(string text, string family, double size)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
            { Started.Set(); if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release background layout calculation."); }
            return inner.Measure(text, family, size);
        }
        public void Dispose() { Release.Set(); Started.Dispose(); Release.Dispose(); }
    }
    private static LayoutSettings ScreenshotColumnSettings => new()
    {
        Arrangement = InformationArrangement.TwoColumns, FontFamily = "Consolas",
        PrimaryFontSize = 26, LensFontSize = 20, SecondaryFontSize = 20, DateFontSize = 20,
        PhotoSideMargin = 26, PhotoTopMargin = 59, PhotoInfoGap = 67, BottomSafeDistance = 47,
        CropExtraPadding = 51, ProtectFourByFive = true, ProtectFiveByFour = false
    };
    private string LongExifSource()
    {
        var path = Path.Combine(root, "long-lens.jpg");
        using var image = new MagickImage(MagickColors.SteelBlue, 1600, 1200);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Panasonic"); exif.SetValue(ExifTag.Model, "DC-GH5");
        exif.SetValue(ExifTag.LensModel, "Panasonic Lumix G Vario 45-200mm f/4-5.6 MEGA O.I.S.");
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:10:12 22:56:00");
        exif.SetValue(ExifTag.FocalLength, new Rational(78, 1));
        exif.SetValue(ExifTag.FNumber, new Rational(10, 1));
        exif.SetValue(ExifTag.ExposureTime, new Rational(1, 125));
        exif.SetValue(ExifTag.ISOSpeedRatings, new ushort[] { 100 });
        image.SetProfile(exif); image.Write(path); return path;
    }

    [Fact]
    public void FirstImportWithOverwideExifSwitchesSelectorShowsWarningAndRendersExportablePhoto() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "fallback-settings.json");
        await new SettingsStore(path).SaveAsync(new PersistedSettings { Layout = ScreenshotColumnSettings });
        using var vm = Create(path); await vm.InitializeAsync(); Assert.Null(vm.PreviewBytes);
        var source = LongExifSource(); var original = File.ReadAllBytes(source);
        await vm.LoadPhotoAsync(source); await Until(() => vm.CanExport);
        Assert.NotNull(vm.PreviewBytes); Assert.Equal(InformationArrangement.Stacked, vm.Arrangement);
        Assert.Equal(InformationArrangement.Stacked, vm.SelectedArrangement!.Value);
        Assert.Equal(LayoutEngine.ColumnsFallbackWarning, vm.ArrangementWarning);
        Assert.Contains("安全檢查失敗，無法使用左右分欄", vm.PreviewCaption); Assert.Empty(vm.ErrorText);
        Assert.Equal("Panasonic DC-GH5", vm.Camera); Assert.Contains("45-200mm", vm.Lens);
        Assert.Equal(26, vm.PhotoSideMargin); Assert.Equal(67, vm.PhotoInfoGap); Assert.Equal(20, vm.LensFontSize);
        await vm.FlushSettingsAsync();
        var saved = await new SettingsStore(path).LoadAsync(); Assert.Equal(InformationArrangement.Stacked, saved.Layout.Arrangement);
        using var restarted = Create(path); await restarted.InitializeAsync(); Assert.Equal(InformationArrangement.Stacked, restarted.Arrangement);
        var destination = Path.Combine(root, "fallback-export.jpg"); vm.ShowGuides = false; await Until(() => vm.CanExport);
        await vm.ExportAsync(destination); Assert.True(File.Exists(destination));
        using var photo = new ImageLoader(new ExifReader()).Load(source);
        var metadata = new DisplayMetadata(vm.Camera, vm.Lens, vm.FocalLength, vm.Aperture, vm.Shutter, vm.Iso, "2026.10.12 22:56");
        var settings = ScreenshotColumnSettings with { Arrangement = InformationArrangement.Stacked, Visibility = new() { Date = true } };
        var expectedLayout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize, metadata, settings);
        Assert.True(expectedLayout.IsValid); Assert.Empty(LayoutValidator.Validate(expectedLayout));
        using var expected = new MagickImage(new ImageRenderer().RenderJpeg(photo, expectedLayout, 95));
        using var actual = new MagickImage(destination); Assert.Equal(0, expected.Compare(actual, ErrorMetric.Absolute));
        Assert.Equal(original, File.ReadAllBytes(source)); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ManualOverflowFallsBackButAlreadyCenteredOrAutoDoesNotRaiseColumnWarning() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.Arrangement = InformationArrangement.TwoColumns; vm.Lens = new string('長', 100);
        await Until(() => vm.CanExport && vm.Arrangement == InformationArrangement.Stacked);
        Assert.Equal(LayoutEngine.ColumnsFallbackWarning, vm.ArrangementWarning);
        vm.Arrangement = InformationArrangement.Auto; await Until(() => vm.CanExport);
        Assert.Empty(vm.ArrangementWarning);
        vm.Arrangement = InformationArrangement.Stacked; await Until(() => vm.CanExport);
        Assert.Empty(vm.ArrangementWarning); Assert.Empty(vm.ErrorText); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void CenteredFallbackThatStillFailsSafetyCannotExport() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.Arrangement = InformationArrangement.TwoColumns;
        vm.ApplyWorkingMargins(new MarginRule { Top = 400, Bottom = 540, Gap = 200 });
        vm.Lens = new string('長', 100);
        await Until(() => vm.Arrangement == InformationArrangement.Stacked && vm.ErrorText.Contains("安全檢查失敗", StringComparison.Ordinal));
        Assert.False(vm.CanExport); Assert.NotEmpty(vm.ArrangementWarning);
        var destination = Path.Combine(root, "must-not-export.jpg"); await vm.ExportAsync(destination); Assert.False(File.Exists(destination));
        vm.ApplyWorkingMargins(new MarginRule()); vm.Lens = "Short lens";
        await Until(() => vm.CanExport); Assert.Empty(vm.ErrorText);
        await vm.FlushSettingsAsync();
    });

    [Fact]
    public void StaleColumnFallbackCannotOverrideNewerUserArrangement() => Ui.Run(async () =>
    {
        using var measurer = new PausedMeasurer(); var renderer = new ImageRenderer();
        using var vm = new PhotoMargin.App.ViewModels.MainViewModel(new ImageLoader(new ExifReader()), new LayoutEngine(measurer),
            new PreviewService(renderer), new ExportService(renderer), new SettingsStore(Path.Combine(root, "stale-settings.json")));
        await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        measurer.Arm(); vm.Arrangement = InformationArrangement.TwoColumns; vm.Lens = new string('長', 100);
        try
        {
            await Until(() => measurer.Started.IsSet);
            // This UI continuation must run while the old calculation is paused.
            vm.Arrangement = InformationArrangement.Auto;
        }
        finally { measurer.Release.Set(); }
        await Until(() => vm.CanExport);
        Assert.Equal(InformationArrangement.Auto, vm.Arrangement); Assert.Empty(vm.ArrangementWarning);
        await vm.FlushSettingsAsync();
    });
}
