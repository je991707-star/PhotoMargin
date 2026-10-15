using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

public static class OrientationPolicy
{
    public const string PortraitUnsupportedMessage = "目前版本以橫向照片為主，直向照片版型將於後續加入";

    public static SizeD GetOrientedSize(int pixelWidth, int pixelHeight, ushort exifOrientation)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "照片尺寸必須大於零。");
        }

        return exifOrientation is 5 or 6 or 7 or 8
            ? new SizeD(pixelHeight, pixelWidth)
            : new SizeD(pixelWidth, pixelHeight);
    }

    public static SourceOrientation Classify(SizeD orientedSize)
    {
        if (!orientedSize.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(orientedSize));
        }

        if (Math.Abs(orientedSize.Width - orientedSize.Height) < 0.001)
        {
            return SourceOrientation.Square;
        }

        return orientedSize.Width > orientedSize.Height
            ? SourceOrientation.Landscape
            : SourceOrientation.Portrait;
    }

    public static bool IsSupported(SizeD orientedSize) => Classify(orientedSize) is not SourceOrientation.Portrait;
}
