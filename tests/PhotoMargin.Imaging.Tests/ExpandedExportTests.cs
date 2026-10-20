using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
#pragma warning disable xUnit1051 // Cancellation has an explicit pre-cancelled-token case below; remaining calls are bounded fixture operations.

namespace PhotoMargin.Imaging.Tests;

public sealed class ExpandedExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PhotoMargin.ExportTests", Guid.NewGuid().ToString("N"));
    public ExpandedExportTests() => Directory.CreateDirectory(root);
    private LoadedPhoto Source()
    {
        var path = Path.Combine(root, "source.jpg");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 900, 600)) image.Write(path);
        return new ImageLoader(new ExifReader()).Load(path);
    }
    private static LayoutResult Layout(LoadedPhoto photo, int size = 1080) => new LayoutEngine(new MagickTextMeasurer()).Calculate(photo.OrientedSize,
        new("Camera", "Long lens name", "35mm", "f/2.8", "1/250s", "ISO 200", "2026.10.20 12:30"), new() { OutputSize = size, Arrangement = InformationArrangement.Stacked });

    [Theory]
    [InlineData(ExportFormat.Jpg, false, 1080)]
    [InlineData(ExportFormat.Png, false, 1080)]
    [InlineData(ExportFormat.WebP, false, 1080)]
    [InlineData(ExportFormat.WebP, true, 1080)]
    [InlineData(ExportFormat.Png, false, 2048)]
    public async Task RealEncodingAndWhitelistedMetadata(ExportFormat format, bool lossless, int size)
    {
        using var source = Source(); var layout = Layout(source, size); Assert.True(layout.IsValid, string.Join(";", layout.Errors));
        var preferences = new ExportPreferences { Format = format, WebpLossless = lossless, IncludeMetadata = true };
        var metadata = new PhotoMetadata { Model = "Panasonic DC-GH5", LensModel = "OLYMPUS M.12-40mm F2.8", CapturedAt = new DateTimeOffset(2026, 10, 20, 12, 30, 0, TimeSpan.FromHours(8)), FocalLengthMm = 35.5, FNumber = 2.8, ExposureSeconds = 1d / 250, Iso = 200 };
        var path = await new ExportService(new ImageRenderer()).ExportAsync(source, layout, Path.Combine(root, "result" + preferences.Extension), preferences, metadata, null);
        using var decoded = new MagickImage(path);
        Assert.Equal((uint)size, decoded.Width); Assert.Equal((uint)size, decoded.Height);
        Assert.Equal(format switch { ExportFormat.Jpg => MagickFormat.Jpeg, ExportFormat.Png => MagickFormat.Png, _ => MagickFormat.WebP }, decoded.Format);
        Assert.NotNull(decoded.GetColorProfile());
        var read = new ExifReader().Read(path);
        Assert.Equal(metadata.Model, read.Model); Assert.Equal(metadata.LensModel, read.LensModel);
        Assert.Equal(35.5, read.FocalLengthMm); Assert.Equal(2.8, read.FNumber); Assert.Equal(200, read.Iso);
        Assert.Equal(metadata.CapturedAt.Value.DateTime, read.CapturedAt!.Value.DateTime);
        Assert.Equal((ushort)1, decoded.GetExifProfile()!.GetValue(ExifTag.Orientation)!.Value);
        Assert.DoesNotContain(decoded.GetExifProfile()!.Values, value => value.Tag.ToString().StartsWith("GPS"));
        var off = new ImageRenderer().RenderExport(source, layout, preferences with { IncludeMetadata = false }, metadata);
        using var stripped = new MagickImage(off); Assert.Null(stripped.GetExifProfile()); Assert.NotNull(stripped.GetColorProfile());
    }

    [Fact]
    public void LosslessPixelsAndLayoutAreIdentical()
    {
        using var source = Source(); var layout = Layout(source); var renderer = new ImageRenderer();
        using var png = new MagickImage(renderer.RenderExport(source, layout, new() { Format = ExportFormat.Png }, null));
        using var webp = new MagickImage(renderer.RenderExport(source, layout, new() { Format = ExportFormat.WebP, WebpLossless = true }, null));
        Assert.Equal(0, png.Compare(webp, ErrorMetric.Absolute));
        using var jpeg = new MagickImage(renderer.RenderExport(source, layout, new() { JpegQuality = 1 }, null));
        Assert.Equal(png.Width, jpeg.Width);
    }

    [Theory]
    [InlineData(ExportFormat.Jpg)]
    [InlineData(ExportFormat.Png)]
    [InlineData(ExportFormat.WebP)]
    public void UnicodeMetadataIsNotSilentlyLost(ExportFormat format)
    {
        using var source = Source();
        var preferences = new ExportPreferences { Format = format, IncludeMetadata = true };
        var metadata = new PhotoMetadata { Model = "相機 GH5", LensModel = "鏡頭 12–40mm" };
        var bytes = new ImageRenderer().RenderExport(source, Layout(source), preferences, metadata);
        var path = Path.Combine(root, "unicode" + preferences.Extension); File.WriteAllBytes(path, bytes);
        var read = new ExifReader().Read(path);
        Assert.Equal(metadata.Model, read.Model); Assert.Equal(metadata.LensModel, read.LensModel);
    }

    [Fact]
    public async Task NumberingCancellationAndBothSourcesProtected()
    {
        using var source = Source(); var layout = Layout(source); var service = new ExportService(new ImageRenderer());
        var destination = Path.Combine(root, "out.jpg"); await File.WriteAllTextAsync(destination, "existing");
        var result = await service.ExportAsync(source, layout, destination, new(), null, null);
        Assert.EndsWith("out (1).jpg", result); Assert.Equal("existing", await File.ReadAllTextAsync(destination));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(source, layout, source.SourcePath, new(), null, null));
        var raw = Path.Combine(root, "raw.jpg"); await File.WriteAllTextAsync(raw, "RAW sentinel");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(source, layout, raw, new(), null, raw));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(source, layout, destination, new(), null, null, cancellation.Token));
        Assert.Empty(Directory.GetFiles(root, "*.tmp")); Assert.Equal("RAW sentinel", await File.ReadAllTextAsync(raw));
    }

    [Theory]
    [InlineData("{name}_framed.jpg.webp", "source_framed.png")]
    [InlineData("CON", "_CON.png")]
    [InlineData("bad:/file?", "bad__file_.png")]
    public void FileNameCleaning(string pattern, string expected) => Assert.Equal(expected,
        new ExportPreferences { Format = ExportFormat.Png, CustomFileName = true, FileNamePattern = pattern }.GetFileName("source.jpg", DateTimeOffset.Now));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
