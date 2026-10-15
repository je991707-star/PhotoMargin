using PhotoMargin.Core.Models;

namespace PhotoMargin.Imaging;

public sealed class MagickTextMeasurer : ITextMeasurer
{
    public SizeD Measure(string text, string fontFamily, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SizeD(0, fontSize);
        }

        var measured = TextGeometry.Measure(text, fontFamily, fontSize);
        return new SizeD(measured.Width, measured.Height);
    }
}
