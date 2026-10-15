using PhotoMargin.Core.Models;

namespace PhotoMargin.Core;

public static class CropGeometry
{
    public static RectD VisibleRect(double canvasSize, double widthToHeightRatio)
    {
        if (canvasSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasSize));
        }

        if (widthToHeightRatio <= 0 || double.IsNaN(widthToHeightRatio) || double.IsInfinity(widthToHeightRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(widthToHeightRatio));
        }

        if (Math.Abs(widthToHeightRatio - 1d) < 0.000001)
        {
            return new RectD(0, 0, canvasSize, canvasSize);
        }

        if (widthToHeightRatio < 1)
        {
            var width = canvasSize * widthToHeightRatio;
            var left = (canvasSize - width) / 2d;
            return new RectD(left, 0, left + width, canvasSize);
        }

        var height = canvasSize / widthToHeightRatio;
        var top = (canvasSize - height) / 2d;
        return new RectD(0, top, canvasSize, top + height);
    }

    public static RectD TextSafeRect(LayoutSettings rawSettings)
    {
        var settings = rawSettings.Validate();
        var size = settings.OutputSize;
        var visible = new RectD(0, 0, size, size);

        if (settings.ProtectFourByFive)
        {
            visible = RectD.Intersect(visible, VisibleRect(size, 4d / 5d));
        }

        if (settings.ProtectFiveByFour)
        {
            visible = RectD.Intersect(visible, VisibleRect(size, 5d / 4d));
        }

        var padding = settings.CropExtraPadding * settings.Scale;
        var safe = visible.Inset(padding);
        var bottom = Math.Min(safe.Bottom, size - settings.BottomSafeDistance * settings.Scale);
        return new RectD(safe.Left, safe.Top, safe.Right, bottom);
    }

}
