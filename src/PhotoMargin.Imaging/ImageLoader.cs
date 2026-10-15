using ImageMagick;
using MetadataExtractor;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class ImageLoader(ExifReader exifReader)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };

    public Task<LoadedPhoto> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, cancellationToken), cancellationToken);

    public LoadedPhoto Load(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到照片檔案。", fullPath);
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(fullPath)))
        {
            throw new NotSupportedException("僅支援 JPG、JPEG 與 PNG。 ");
        }

        cancellationToken.ThrowIfCancellationRequested();
        MagickImage? image = null;
        try
        {
            image = new MagickImage(fullPath);
            if (image.Width == 0 || image.Height == 0)
            {
                throw new InvalidDataException("照片尺寸無效。");
            }

            var original = new SizeD(image.Width, image.Height);
            var hadProfile = image.GetColorProfile() is not null;
            ConvertToSrgb(image);
            image.AutoOrient();
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            cancellationToken.ThrowIfCancellationRequested();

            PhotoMetadata metadata;
            try
            {
                metadata = exifReader.Read(fullPath);
            }
            catch (ImageProcessingException)
            {
                metadata = new PhotoMetadata();
            }

            var loaded = new LoadedPhoto(fullPath, image, original, metadata, hadProfile, true);
            image = null;
            return loaded;
        }
        catch (MagickException exception)
        {
            throw new InvalidDataException("無法解碼照片，檔案可能已損壞或格式不受支援。", exception);
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static void ConvertToSrgb(MagickImage image)
    {
        var embedded = image.GetColorProfile();
        if (embedded is not null)
        {
            image.TransformColorSpace(embedded, ColorProfiles.SRGB);
            return;
        }

        if (image.ColorSpace is ColorSpace.CMYK)
        {
            image.TransformColorSpace(ColorProfiles.USWebCoatedSWOP, ColorProfiles.SRGB);
            return;
        }

        image.ColorSpace = ColorSpace.sRGB;
        image.SetProfile(ColorProfiles.SRGB);
    }
}
