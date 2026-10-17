using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging.Tests;

public sealed class LensFontRenderingTests
{
    [Theory]
    [InlineData(8, InformationArrangement.TwoColumns)] [InlineData(8, InformationArrangement.Stacked)]
    [InlineData(12, InformationArrangement.TwoColumns)] [InlineData(12, InformationArrangement.Stacked)]
    [InlineData(16, InformationArrangement.TwoColumns)] [InlineData(16, InformationArrangement.Stacked)]
    [InlineData(18, InformationArrangement.TwoColumns)] [InlineData(18, InformationArrangement.Stacked)]
    public async Task RealLensGlyphsUseRequestedSizeAndSharedPreviewExportGeometry(double fontSize, InformationArrangement arrangement)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-lens.png");
        try
        {
            var token = TestContext.Current.CancellationToken;
            using (var image = new MagickImage(MagickColors.White, 1600, 900)) image.Write(path);
            using var photo = new ImageLoader(new ExifReader()).Load(path, token);
            var renderer = new ImageRenderer(); var engine = new LayoutEngine(new MagickTextMeasurer());
            const string lens = "Panasonic Lumix G Vario 45-200mm f/4-5.6 MEGA O.I.S.";
            foreach (var output in new[] { 1080, 2048 })
            {
                var layout = engine.Calculate(photo.OrientedSize, new("", lens, "", "", "", "", ""),
                    new() { LensFontSize = fontSize, OutputSize = output, Arrangement = arrangement, FontFamily = "Consolas" });
                Assert.True(layout.IsValid, string.Join(";", layout.Errors)); var run = Assert.Single(layout.TextRuns);
                Assert.Equal(lens, run.Text); Assert.Equal(fontSize * output / 1080d, run.FontSize, 7);
                var expected = new MagickTextMeasurer().Measure(lens, "Consolas", run.FontSize);
                Assert.Equal(expected.Height, run.Bounds.Height, 7); Assert.Equal(expected.Width, run.Bounds.Width, 7);
                using var png = new MagickImage(renderer.RenderPng(photo, layout, token));
                using var preview = new MagickImage(await renderer.RenderPreviewAsync(photo, layout, false, token));
                Assert.Equal(0, png.Compare(preview, ErrorMetric.Absolute));
                var ink = png.BoundingBox; Assert.NotNull(ink); Assert.True(ink.Height < 18 * output / 1080d + 3 || fontSize >= 18);
                using var jpg = new MagickImage(renderer.RenderJpeg(photo, layout, 95, token));
                Assert.Equal((uint)output, jpg.Width); Assert.Equal((uint)output, jpg.Height);
            }
        }
        finally { File.Delete(path); }
    }
}
