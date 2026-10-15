using ImageMagick;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class LoadedPhoto : IDisposable
{
    internal LoadedPhoto(
        string sourcePath,
        MagickImage image,
        SizeD originalSize,
        PhotoMetadata metadata,
        bool hadEmbeddedColorProfile,
        bool colorConverted)
    {
        SourcePath = sourcePath;
        Image = image;
        OriginalSize = originalSize;
        Metadata = metadata;
        HadEmbeddedColorProfile = hadEmbeddedColorProfile;
        ColorConverted = colorConverted;
    }

    internal MagickImage Image { get; }
    public string SourcePath { get; }
    public SizeD OriginalSize { get; }
    public SizeD OrientedSize => new(Image.Width, Image.Height);
    public SourceOrientation Orientation => OrientationPolicy.Classify(OrientedSize);
    public PhotoMetadata Metadata { get; }
    public bool HadEmbeddedColorProfile { get; }
    public bool ColorConverted { get; }

    public void Dispose() => Image.Dispose();
}
