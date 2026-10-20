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
        => RenderExport(photo, layout, new ExportPreferences { JpegQuality = quality }, null, cancellationToken);

    public byte[] RenderExport(LoadedPhoto photo, LayoutResult layout, ExportPreferences preferences, PhotoMetadata? metadata, CancellationToken cancellationToken = default)
    {
        using var image = Compose(photo, layout, cancellationToken);
        image.Strip();
        image.SetProfile(ColorProfiles.SRGB);
        image.Orientation = OrientationType.TopLeft;
        preferences = preferences.Validate();
        if (preferences.IncludeMetadata && metadata is null) throw new InvalidOperationException("沒有可供寫入的拍攝資訊快照。");
        image.Format = preferences.Format switch { ExportFormat.Png => MagickFormat.Png, ExportFormat.WebP => MagickFormat.WebP, _ => MagickFormat.Jpeg };
        image.Quality = (uint)(preferences.Format == ExportFormat.WebP ? preferences.WebpQuality : preferences.Format == ExportFormat.Png ? 90 : preferences.JpegQuality);
        if (preferences.Format == ExportFormat.WebP) image.Settings.SetDefine(MagickFormat.WebP, "lossless", preferences.WebpLossless ? "true" : "false");
        if (preferences.Format == ExportFormat.Png)
        {
            // Strip leaves a PNG artifact which overrides encoder options.
            image.SetArtifact("png:include-chunk", "iCCP,eXIf");
            image.SetArtifact("png:exclude-chunk", "sRGB,date,time");
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", "true");
        }
        if (preferences.IncludeMetadata && metadata is not null)
        {
            var exif = new ExifProfile();
            if (!string.IsNullOrWhiteSpace(metadata.Model)) exif.SetValue(ExifTag.Model, metadata.Model);
            if (!string.IsNullOrWhiteSpace(metadata.Make)) exif.SetValue(ExifTag.Make, metadata.Make);
            if (!string.IsNullOrWhiteSpace(metadata.LensModel)) exif.SetValue(ExifTag.LensModel, metadata.LensModel);
            if (metadata.FocalLengthMm is > 0) exif.SetValue(ExifTag.FocalLength, new Rational(metadata.FocalLengthMm.Value));
            if (metadata.FNumber is > 0) exif.SetValue(ExifTag.FNumber, new Rational(metadata.FNumber.Value));
            if (metadata.ExposureSeconds is > 0) exif.SetValue(ExifTag.ExposureTime, new Rational(metadata.ExposureSeconds.Value));
            if (metadata.Iso is > 0 and <= ushort.MaxValue) exif.SetValue(ExifTag.ISOSpeedRatings, new[] { (ushort)metadata.Iso.Value });
            else if (metadata.Iso is > ushort.MaxValue) throw new InvalidOperationException("ISO 超出目前 EXIF 編碼範圍。");
            if (metadata.CapturedAt is { } date) exif.SetValue(ExifTag.DateTimeOriginal, date.ToString("yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            exif.SetValue(ExifTag.Orientation, (ushort)1);
            image.SetProfile(exif);
        }
        cancellationToken.ThrowIfCancellationRequested();
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

        if (layout.ProtectFourByFive) DrawRectangle(image, portrait, "#5588CC", 2);
        if (layout.ProtectFiveByFour) DrawRectangle(image, landscape, "#DD8A34", 2);
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
