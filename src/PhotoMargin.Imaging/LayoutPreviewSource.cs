using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

// Settings-only, bounded-resolution copies. Never modifies the loaded original.
public static class LayoutPreviewSource
{
    public static LoadedPhoto Create(LoadedPhoto? source)
    {
        MagickImage image;
        if (source is null)
        {
            using var stream = typeof(LayoutPreviewSource).Assembly.GetManifestResourceStream("PhotoMargin.Demo4x3.png")
                ?? throw new InvalidOperationException("找不到內建示範圖片。");
            image = new MagickImage(stream);
        }
        else image = (MagickImage)source.Image.Clone();
        try
        {
            if (image.Width > 1280 || image.Height > 1280) image.Resize(1280, 1280);
            image.ResetPage();
            return new LoadedPhoto("settings-preview", image, new(image.Width, image.Height), new PhotoMetadata(), false, true);
        }
        catch { image.Dispose(); throw; }
    }
    public static LoadedPhoto Crop(LoadedPhoto source, SourceAspect aspect)
    {
        var image = (MagickImage)source.Image.Clone();
        try
        {
            var (w, h) = aspect.Dimensions();
            var factor = Math.Max(1, Math.Min((int)image.Width / w, (int)image.Height / h));
            var width = (uint)(factor * w); var height = (uint)(factor * h);
            // Tiny inputs cannot supply an integer-ratio crop; uniformly upscale first.
            if (width > image.Width || height > image.Height)
                image.Resize(new MagickGeometry(width, height) { FillArea = true });
            image.Crop(new MagickGeometry((int)(image.Width - width) / 2, (int)(image.Height - height) / 2, width, height));
            image.ResetPage();
            return new LoadedPhoto("settings-preview", image, source.OriginalSize, new PhotoMetadata(), false, true);
        }
        catch { image.Dispose(); throw; }
    }
    public static DisplayMetadata DemoMetadata { get; } = new(
        "Panasonic DC-GH5", "LEICA DG 12-60mm F2.8-4.0", "35mm", "f/2.8", "1/250s", "ISO 200", "2023.11.08 16:15");
}
