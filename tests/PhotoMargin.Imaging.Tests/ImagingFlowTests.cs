using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhotoMargin.Imaging.Tests;

public sealed class ImagingFlowTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "PhotoMargin.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> ownedJunctions = [];

    public ImagingFlowTests() => Directory.CreateDirectory(tempDirectory);

    [Fact]
    public void TransparentPngIsFlattenedAndExportsJpeg()
    {
        var source = Path.Combine(tempDirectory, "透明 測試.png");
        using (var input = new MagickImage(MagickColors.Transparent, 1200, 800))
        {
            new ImageMagick.Drawing.Drawables()
                .FillColor(MagickColors.Red)
                .Rectangle(100, 100, 1100, 700)
                .Draw(input);
            input.Write(source);
        }

        var loader = new ImageLoader(new ExifReader());
        using var photo = loader.Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(
            photo.OrientedSize,
            new DisplayMetadata("測試相機", "測試鏡頭", "35mm", "f/4", "1/125s", "ISO 100", ""),
            new LayoutSettings());
        var jpeg = new ImageRenderer().RenderJpeg(photo, layout, 95, TestContext.Current.CancellationToken);
        using var output = new MagickImage(jpeg);

        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        Assert.Equal(1080u, output.Width);
        Assert.Equal(1080u, output.Height);
        Assert.Equal(MagickFormat.Jpeg, output.Format);
        Assert.False(output.HasAlpha);
        Assert.Null(output.GetExifProfile());
        Assert.NotNull(output.GetColorProfile());
    }

    [Fact]
    public async Task ExportRefusesToOverwriteSource()
    {
        var source = Path.Combine(tempDirectory, "source.jpg");
        using (var input = new MagickImage(MagickColors.Blue, 1200, 800))
        {
            input.Write(source);
        }

        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(
            photo.OrientedSize,
            new DisplayMetadata("", "", "", "", "", "", ""),
            new LayoutSettings());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExportService(new ImageRenderer()).ExportJpegAsync(photo, layout, source, 95, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Microsoft JhengHei UI", "測試相機")]
    [InlineData("Consolas", "繁體中文與 Latin")]
    [InlineData("Times New Roman", "j")]
    public void SelectedPhotoFontDrawsRealGlyphsInsideSafetyBounds(string font, string camera)
    {
        var source = Path.Combine(tempDirectory, "glyphs.png");
        using (var input = new MagickImage(MagickColors.White, 1200, 800)) input.Write(source);
        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
            new DisplayMetadata(camera, "", "", "", "", "", ""), new LayoutSettings { FontFamily = font });
        using var output = new MagickImage(new ImageRenderer().RenderPng(photo, layout, TestContext.Current.CancellationToken));
        var ink = output.BoundingBox;
        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        Assert.NotNull(ink);
        Assert.True(ink.X >= layout.TextSafeBounds.Left && ink.Y >= layout.TextSafeBounds.Top);
        Assert.True(ink.X + ink.Width <= layout.TextSafeBounds.Right && ink.Y + ink.Height <= layout.TextSafeBounds.Bottom);
    }

    [Theory]
    [InlineData(InformationArrangement.Auto)]
    [InlineData(InformationArrangement.TwoColumns)]
    [InlineData(InformationArrangement.Stacked)]
    public async Task EveryArrangementRendersTheSameCompositionInPreviewAndExport(InformationArrangement arrangement)
    {
        var token = TestContext.Current.CancellationToken;
        var source = Path.Combine(tempDirectory, "arrangement.png");
        using (var input = new MagickImage(MagickColors.White, 1200, 800)) input.Write(source);
        using var photo = new ImageLoader(new ExifReader()).Load(source, token);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
            new DisplayMetadata("測試相機", "測試鏡頭", "35mm", "f/4", "1/125s", "ISO 200", "2026.10.16 09:30"),
            new LayoutSettings { Arrangement = arrangement, Visibility = new FieldVisibility { Date = true } });
        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        var renderer = new ImageRenderer();
        using var preview = new MagickImage(await renderer.RenderPreviewAsync(photo, layout, false, token));
        using var composed = new MagickImage(renderer.RenderPng(photo, layout, token));
        Assert.Equal(0, preview.Compare(composed, ErrorMetric.Absolute));
        var ink = preview.BoundingBox;
        Assert.NotNull(ink);
        Assert.True(ink.X >= layout.TextSafeBounds.Left && ink.Y >= layout.TextSafeBounds.Top);
        Assert.True(ink.X + ink.Width <= layout.TextSafeBounds.Right && ink.Y + ink.Height <= layout.TextSafeBounds.Bottom);
        var destination = Path.Combine(tempDirectory, "arrangement.jpg");
        await new ExportService(renderer).ExportJpegAsync(photo, layout, destination, 95, token);
        Assert.Equal(renderer.RenderJpeg(photo, layout, 95, token), await File.ReadAllBytesAsync(destination, token));
    }

    [Fact]
    public void CmykImageConvertsToSrgb()
    {
        var source = Path.Combine(tempDirectory, "cmyk.jpg");
        using (var input = new MagickImage(MagickColors.Cyan, 1200, 800))
        {
            input.ColorSpace = ColorSpace.CMYK;
            input.Write(source);
        }

        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        Assert.Equal(ColorSpace.sRGB, photo.ImageForTest().ColorSpace);
    }

    [Fact]
    public void RawPortraitWithQuarterTurnOrientationLoadsAsLandscape()
    {
        var source = Path.Combine(tempDirectory, "orientation-6.jpg");
        using (var input = new MagickImage(MagickColors.Orange, 800, 1200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            input.SetProfile(exif);
            input.Orientation = OrientationType.RightTop;
            input.Write(source);
        }

        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);

        Assert.Equal(new SizeD(1200, 800), photo.OrientedSize);
        Assert.Equal(SourceOrientation.Landscape, photo.Orientation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewKeepsCompleteSquareCanvas(bool showGuides)
    {
        var source = Path.Combine(tempDirectory, "preview.png");
        using (var input = new MagickImage(MagickColors.Blue, 1200, 800))
        {
            input.Write(source);
        }
        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
            new DisplayMetadata("", "", "", "", "", "", ""), new LayoutSettings());
        var preview = await new ImageRenderer().RenderPreviewAsync(photo, layout, showGuides,
            TestContext.Current.CancellationToken);
        using var output = new MagickImage(preview);
        Assert.Equal(1080u, output.Width);
        Assert.Equal(1080u, output.Height);
    }

    [Theory]
    [InlineData(true, "DateTimeOriginal")]
    [InlineData(false, "DateTime")]
    public void ReadsOriginalDateOrEmbeddedFallback(bool useOriginal, string expectedSource)
    {
        var source = Path.Combine(tempDirectory, "date.jpg");
        using (var input = new MagickImage(MagickColors.Green, 1200, 800))
        {
            var exif = new ExifProfile();
            exif.SetValue(useOriginal ? ExifTag.DateTimeOriginal : ExifTag.DateTime, "2023:11:08 16:15:20");
            input.SetProfile(exif);
            input.Write(source);
        }
        var metadata = new ExifReader().Read(source);
        Assert.Equal(expectedSource, metadata.DateSource);
        Assert.NotNull(metadata.CapturedAt);
        Assert.Equal(2023, metadata.CapturedAt.Value.Year);
        Assert.Equal(11, metadata.CapturedAt.Value.Month);
        Assert.Equal(8, metadata.CapturedAt.Value.Day);
        Assert.Equal(16, metadata.CapturedAt.Value.Hour);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRefusesPhysicalSourceAliasesWithoutChangingSource(bool junction)
    {
        var actualDirectory = Path.Combine(tempDirectory, "real"); Directory.CreateDirectory(actualDirectory);
        var source = Path.Combine(actualDirectory, "source.jpg");
        using (var input = new MagickImage(MagickColors.Blue, 900, 600)) input.Write(source);
        string alias;
        if (junction)
        {
            var aliasDirectory = Path.Combine(tempDirectory, "alias");
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
            start.ArgumentList.Add($"$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '{aliasDirectory.Replace("'", "''")}' -Target '{actualDirectory.Replace("'", "''")}' | Out-Null");
            using var process = Process.Start(start)!; await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
            ownedJunctions.Add(aliasDirectory);
            alias = Path.Combine(aliasDirectory, "source.jpg");
        }
        else
        {
            alias = Path.Combine(tempDirectory, "hardlink.jpg");
            Assert.True(CreateHardLink(alias, source, IntPtr.Zero), "Cannot create an owned NTFS hard-link fixture.");
        }
        var before = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
            new DisplayMetadata("", "", "", "", "", "", ""), new LayoutSettings());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExportService(new ImageRenderer()).ExportJpegAsync(photo, layout, alias, 95, TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancelledExportPreservesExistingOutput()
    {
        var source = Path.Combine(tempDirectory, "cancel-source.png");
        using (var input = new MagickImage(MagickColors.Blue, 900, 600)) input.Write(source);
        var destination = Path.Combine(tempDirectory, "previous.jpg");
        var original = new byte[] { 1, 2, 3 }; await File.WriteAllBytesAsync(destination, original, TestContext.Current.CancellationToken);
        using var photo = new ImageLoader(new ExifReader()).Load(source, TestContext.Current.CancellationToken);
        var layout = new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
            new DisplayMetadata("", "", "", "", "", "", ""), new LayoutSettings());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExportService(new ImageRenderer()).ExportJpegAsync(photo, layout, destination, 95, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp"));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newName, string existingName, IntPtr attributes);

    public void Dispose()
    {
        // Remove only the owned junction itself before its target, never recurse into it.
        foreach (var junction in ownedJunctions) Directory.Delete(junction, false);
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
    }
}

internal static class LoadedPhotoTestExtensions
{
    public static MagickImage ImageForTest(this LoadedPhoto photo)
    {
        var property = typeof(LoadedPhoto).GetProperty("Image", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (MagickImage)property.GetValue(photo)!;
    }
}
