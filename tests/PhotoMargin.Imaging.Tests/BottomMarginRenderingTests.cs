using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging.Tests;

public sealed class BottomMarginRenderingTests
{
    [Theory]
    [InlineData("Consolas", InformationArrangement.TwoColumns)]
    [InlineData("Consolas", InformationArrangement.Stacked)]
    [InlineData("Microsoft JhengHei UI", InformationArrangement.TwoColumns)]
    [InlineData("Microsoft JhengHei UI", InformationArrangement.Stacked)]
    [InlineData("Times New Roman", InformationArrangement.TwoColumns)]
    [InlineData("Times New Roman", InformationArrangement.Stacked)]
    public async Task VisibleInkBottomTracksMarginAtBothResolutionsWithoutClipping(string font, InformationArrangement arrangement)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-bottom.png");
        try
        {
            using (var image = new MagickImage(MagickColors.White, 1600, 900)) image.Write(path);
            var token = TestContext.Current.CancellationToken;
            using var photo = new ImageLoader(new ExifReader()).Load(path, token);
            var renderer = new ImageRenderer(); var engine = new LayoutEngine(new MagickTextMeasurer());
            var metadata = new DisplayMetadata("相機 gypq", "Lens jg", "35mm", "f/2.8", "1/250s", "ISO 200", "2026.10.17 12:34");
            foreach (var output in new[] { 1080, 2048 })
            foreach (var margin in new[] { 0, 144 })
            {
                var layout = engine.Calculate(photo.OrientedSize, metadata, new()
                {
                    OutputSize = output, BottomSafeDistance = margin, Arrangement = arrangement, FontFamily = font,
                    PrimaryFontSize = 36, LensFontSize = 24, SecondaryFontSize = 22, DateFontSize = 24, Visibility = new() { Date = true }
                });
                Assert.True(layout.IsValid, string.Join(";", layout.Errors));
                using var image = new MagickImage(renderer.RenderPng(photo, layout, token));
                var ink = image.BoundingBox; Assert.NotNull(ink);
                var actual = output - (ink.Y + ink.Height);
                Assert.InRange(actual, margin * output / 1080d, margin * output / 1080d + 3);
                using var preview = new MagickImage(await renderer.RenderPreviewAsync(photo, layout, false, token));
                Assert.Equal(0, image.Compare(preview, ErrorMetric.Absolute));
                using var jpeg = new MagickImage(renderer.RenderJpeg(photo, layout, 95, token));
                Assert.Equal((uint)output, jpeg.Width); Assert.Equal((uint)output, jpeg.Height);
            }
            // An integer translation from an interior position must preserve every
            // glyph pixel at margin zero, including descenders and Chinese ink.
            MagickImage? previous = null;
            try
            {
                foreach (var margin in new[] { 144, 0 })
                {
                    var layout = engine.Calculate(photo.OrientedSize, metadata, new()
                    { BottomSafeDistance = margin, FontFamily = font, Arrangement = arrangement, Visibility = new() { Date = true } });
                    using var output = new MagickImage(renderer.RenderPng(photo, layout, token));
                    output.Crop(output.BoundingBox!); output.ResetPage();
                    if (previous is not null) Assert.Equal(0, previous.Compare(output, ErrorMetric.Absolute));
                    previous?.Dispose(); previous = (MagickImage)output.Clone();
                }
            }
            finally { previous?.Dispose(); }
        }
        finally { File.Delete(path); }
    }
}
