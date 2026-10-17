using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class BottomPlacementTests
{
    private sealed class Measurer : ITextMeasurer
    {
        public SizeD Measure(string text, string family, double size) => new(text.Length * size * 0.5, size);
    }
    private readonly LayoutEngine engine = new(new Measurer());
    private static readonly DisplayMetadata Metadata = new("Body", "Lens", "35mm", "f/4", "1/250s", "ISO 200", "Date");

    [Theory]
    [InlineData(InformationArrangement.TwoColumns)]
    [InlineData(InformationArrangement.Stacked)]
    public void BottomMarginAnchorsMeasuredTextAndPreservesExactPhotoGap(InformationArrangement arrangement)
    {
        foreach (var size in new[] { 1080, 2048 })
        foreach (var margin in new[] { 0, 72, 144 })
        foreach (var visibility in new[] { new FieldVisibility { Date = true }, new FieldVisibility { Lens = false, Exposure = false } })
        {
            var settings = new LayoutSettings { OutputSize = size, Arrangement = arrangement, BottomSafeDistance = margin,
                PhotoInfoGap = 39, ExposureItemGap = 16, PhotoTopMargin = 100, Visibility = visibility };
            var result = engine.Calculate(new(1600, 900), Metadata, settings); var scale = size / 1080d;
            Assert.True(result.IsValid, string.Join(";", result.Errors));
            Assert.Equal(size - margin * scale, result.TextRuns.Max(run => run.Bounds.Bottom), 7);
            Assert.Equal(39 * scale, result.TextRuns.Min(run => run.Bounds.Top) - result.PhotoBounds.Bottom, 7);
            Assert.Equal(16d / 9, result.PhotoBounds.Width / result.PhotoBounds.Height, 8);
            Assert.True(result.PhotoBounds.Top >= 100 * scale - 0.001);
            Assert.Empty(LayoutValidator.Validate(result));
        }
    }

    [Fact]
    public void ChangingHorizontalInsetDoesNotMoveBottomEvenWhenItReflowsInformation()
    {
        foreach (var inset in new[] { 0, 100, 200 })
        {
            var result = engine.Calculate(new(1600, 900), Metadata, new()
            { Arrangement = InformationArrangement.Stacked, CropExtraPadding = inset, BottomSafeDistance = 0 });
            Assert.True(result.IsValid); Assert.Equal(1080, result.TextRuns.Max(run => run.Bounds.Bottom), 8);
        }
    }

    [Fact]
    public void IncreasingVerticalGapMovesPhotoButNotAnchoredTextOrHorizontalItemSpacing()
    {
        LayoutResult Calculate(double gap) => engine.Calculate(new(1600, 900), Metadata, new()
        { Arrangement = InformationArrangement.Stacked, PhotoInfoGap = gap, ExposureItemGap = 29, BottomSafeDistance = 72 });
        var narrow = Calculate(10); var wide = Calculate(60);
        Assert.True(narrow.IsValid); Assert.True(wide.IsValid);
        Assert.Equal(narrow.TextRuns, wide.TextRuns); Assert.Equal(narrow.PhotoBounds.Height, wide.PhotoBounds.Height, 8);
        Assert.Equal(50, narrow.PhotoBounds.Bottom - wide.PhotoBounds.Bottom, 8);
        var exposure = wide.TextRuns.Where(run => run.Text is "35mm" or "f/4").ToArray();
        Assert.Equal(29, exposure[1].Bounds.Left - exposure[0].Bounds.Right, 8);
    }

    [Fact]
    public void ExplicitLandscapeProtectionStillLimitsBottomWhileDefaultDoesNot()
    {
        var normal = engine.Calculate(new(1600, 900), Metadata, new() { BottomSafeDistance = 0 });
        var protectedResult = engine.Calculate(new(1600, 900), Metadata, new() { BottomSafeDistance = 0, ProtectFiveByFour = true });
        Assert.Equal(1080, normal.TextRuns.Max(run => run.Bounds.Bottom), 8);
        Assert.Equal(972, protectedResult.TextRuns.Max(run => run.Bounds.Bottom), 8);
        Assert.True(protectedResult.IsValid); Assert.True(protectedResult.ProtectFiveByFour); Assert.False(normal.ProtectFiveByFour);
    }

    [Fact]
    public void NoInformationLeavesNoGapOrInvisibleContainerAndCentersWholePhoto()
    {
        var result = engine.Calculate(new(1600, 900), Metadata, new()
        { BottomSafeDistance = 540, PhotoInfoGap = 200, Visibility = new() { Camera = false, Lens = false, Exposure = false, Date = false } });
        Assert.True(result.IsValid); Assert.Empty(result.TextRuns);
        Assert.Equal(540, (result.PhotoBounds.Top + result.PhotoBounds.Bottom) / 2, 8);
    }
}
