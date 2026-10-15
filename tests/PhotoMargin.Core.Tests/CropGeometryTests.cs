using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class CropGeometryTests
{
    [Fact]
    public void FourByFiveCropCutsLeftAndRight()
    {
        var result = CropGeometry.VisibleRect(1080, 4d / 5d);
        Assert.Equal(new RectD(108, 0, 972, 1080), result);
    }

    [Fact]
    public void FiveByFourCropCutsTopAndBottom()
    {
        var result = CropGeometry.VisibleRect(1080, 5d / 4d);
        Assert.Equal(new RectD(0, 108, 1080, 972), result);
    }

    [Fact]
    public void BothProtectionsYieldSpecifiedSafeArea()
    {
        var result = CropGeometry.TextSafeRect(new LayoutSettings());
        Assert.Equal(new RectD(132, 132, 948, 948), result);
    }

    [Fact]
    public void SafeAreaScalesAt2048()
    {
        var result = CropGeometry.TextSafeRect(new LayoutSettings { OutputSize = 2048 });
        var factor = 2048d / 1080d;
        Assert.Equal(132 * factor, result.Left, 6);
        Assert.Equal(948 * factor, result.Bottom, 6);
    }
}
