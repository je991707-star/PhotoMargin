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
    public void DefaultProtectionCropsOnlyHorizontally()
    {
        var result = CropGeometry.TextSafeRect(new LayoutSettings());
        Assert.Equal(new RectD(132, 0, 948, 1008), result);
        Assert.False(new LayoutSettings().ProtectFiveByFour);
    }

    [Fact]
    public void SafeAreaScalesAt2048()
    {
        var result = CropGeometry.TextSafeRect(new LayoutSettings { OutputSize = 2048 });
        var factor = 2048d / 1080d;
        Assert.Equal(132 * factor, result.Left, 6);
        Assert.Equal(1008 * factor, result.Bottom, 6);
    }

    [Fact]
    public void HorizontalTextInsetNeverChangesTopOrBottomAndExplicitLandscapeCropStillWorks()
    {
        foreach (var landscape in new[] { false, true })
        {
            var settings = new LayoutSettings { BottomSafeDistance = 0, CropExtraPadding = 0, ProtectFiveByFour = landscape };
            var none = CropGeometry.TextSafeRect(settings);
            var inset = CropGeometry.TextSafeRect(settings with { CropExtraPadding = 200 });
            Assert.Equal(none.Top, inset.Top); Assert.Equal(none.Bottom, inset.Bottom);
            Assert.Equal(none.Left + 200, inset.Left); Assert.Equal(none.Right - 200, inset.Right);
            Assert.Equal(landscape ? 972 : 1080, inset.Bottom);
            Assert.False(inset.IsEmpty);
        }
    }

    [Fact]
    public void NonFiniteInputsCannotCreateInvalidSafeRectangles()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var safe = CropGeometry.TextSafeRect(new() { BottomSafeDistance = bad, CropExtraPadding = bad });
            Assert.True(double.IsFinite(safe.Width) && double.IsFinite(safe.Height)); Assert.False(safe.IsEmpty);
        }
    }
}
