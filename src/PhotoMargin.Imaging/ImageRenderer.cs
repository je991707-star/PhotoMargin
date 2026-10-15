using ImageMagick;
using ImageMagick.Drawing;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ImageRenderer
{
    public Task<byte[]> RenderPreviewAsync(
        LoadedPhoto photo,
        LayoutResult layout,
        bool showGuides,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RenderPreview(photo, layout, showGuides, cancellationToken), cancellationToken);

    public byte[] RenderJpeg(LoadedPhoto photo, LayoutResult layout, int quality, CancellationToken cancellationToken = default)
    {
        using var image = Compose(photo, layout, cancellationToken);
        image.Strip();
        image.SetProfile(ColorProfiles.SRGB);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)Math.Clamp(quality, 50, 100);
        return image.ToByteArray();
    }

    public byte[] RenderPng(LoadedPhoto photo, LayoutResult layout, CancellationToken cancellationToken = default)
    {
        using var image = Compose(photo, layout, cancellationToken);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    private byte[] RenderPreview(LoadedPhoto photo, LayoutResult layout, bool showGuides, CancellationToken cancellationToken)
    {
        using var image = Compose(photo, layout, cancellationToken);
        if (showGuides)
        {
            DrawGuides(image, layout);
        }

        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    private static MagickImage Compose(LoadedPhoto photo, LayoutResult layout, CancellationToken cancellationToken)
    {
        if (!layout.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, layout.Errors));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var canvas = new MagickImage(MagickColors.White, (uint)layout.CanvasSize, (uint)layout.CanvasSize)
        {
            ColorSpace = ColorSpace.sRGB
        };

        try
        {
            using var resized = (MagickImage)photo.Image.Clone();
            resized.Resize((uint)Math.Round(layout.PhotoBounds.Width), (uint)Math.Round(layout.PhotoBounds.Height));
            canvas.Composite(resized, (int)Math.Round(layout.PhotoBounds.Left), (int)Math.Round(layout.PhotoBounds.Top), CompositeOperator.Over);

            foreach (var run in layout.TextRuns)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var measured = TextGeometry.Measure(run.Text, run.FontFamily, run.FontSize);

                new Drawables()
                    .Font(measured.Font)
                    .FontPointSize(run.FontSize)
                    .FillColor(new MagickColor(run.Color))
                    .StrokeColor(MagickColors.Transparent)
                    .Text(run.Bounds.Left + measured.OriginX, run.Bounds.Top + measured.Baseline, run.Text)
                    .Draw(canvas);
            }

            return canvas;
        }
        catch
        {
            canvas.Dispose();
            throw;
        }
    }

    private static void DrawGuides(MagickImage image, LayoutResult layout)
    {
        var portrait = CropGeometry.VisibleRect(layout.CanvasSize, 4d / 5d);
        var landscape = CropGeometry.VisibleRect(layout.CanvasSize, 5d / 4d);

        DrawRectangle(image, portrait, "#5588CC", 2);
        DrawRectangle(image, landscape, "#DD8A34", 2);
        DrawRectangle(image, layout.TextSafeBounds, "#37A866", 2);
    }

    private static void DrawRectangle(MagickImage image, RectD rect, string color, double width)
    {
        new Drawables()
            .FillColor(MagickColors.Transparent)
            .StrokeColor(new MagickColor(color))
            .StrokeWidth(width)
            .Rectangle(rect.Left, rect.Top, rect.Right, rect.Bottom)
            .Draw(image);
    }
}
