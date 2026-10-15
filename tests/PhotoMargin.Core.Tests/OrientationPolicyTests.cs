using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class OrientationPolicyTests
{
    [Theory]
    [InlineData(1, 4000, 3000)]
    [InlineData(2, 4000, 3000)]
    [InlineData(3, 4000, 3000)]
    [InlineData(4, 4000, 3000)]
    public void NonQuarterTurnOrientationKeepsDimensions(ushort orientation, double width, double height)
    {
        Assert.Equal(new SizeD(width, height), OrientationPolicy.GetOrientedSize(4000, 3000, orientation));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void QuarterTurnOrientationSwapsDimensions(ushort orientation)
    {
        var corrected = OrientationPolicy.GetOrientedSize(3000, 4000, orientation);
        Assert.Equal(new SizeD(4000, 3000), corrected);
        Assert.True(OrientationPolicy.IsSupported(corrected));
    }

    [Fact]
    public void PortraitIsNotSupportedInMvp() =>
        Assert.False(OrientationPolicy.IsSupported(new SizeD(3000, 4000)));
}
