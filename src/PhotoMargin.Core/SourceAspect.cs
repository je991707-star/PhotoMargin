using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

// Source demonstration / preset configuration, never the square output canvas.
public enum SourceAspect { SixteenByNine, FiveByFour, FourByThree, ThreeByTwo }

public static class SourceAspects
{
    public static (int Width, int Height) Dimensions(this SourceAspect aspect) => aspect switch
    {
        SourceAspect.SixteenByNine => (16, 9), SourceAspect.FiveByFour => (5, 4),
        SourceAspect.ThreeByTwo => (3, 2), _ => (4, 3)
    };
    public static SourceAspect Closest(SizeD orientedSize)
    {
        if (!orientedSize.IsPositive) return SourceAspect.FourByThree;
        // Nearest width/height ratio also tolerates rounded camera dimensions.
        return Enum.GetValues<SourceAspect>().MinBy(aspect =>
        {
            var (w, h) = aspect.Dimensions();
            return Math.Abs(orientedSize.Width / orientedSize.Height - (double)w / h);
        });
    }
}
