using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class ExposureSpacingTests
{
    private sealed class Measurer : ITextMeasurer
    {
        public SizeD Measure(string text, string family, double size) => new(text.Length * size * 0.5, size);
    }
    private readonly LayoutEngine engine = new(new Measurer());
    private static readonly DisplayMetadata Metadata = new("Body", "Lens", "35mm", "f/4", "1/250s", "ISO 200", "");
    private LayoutResult Calculate(double gap, InformationArrangement arrangement = InformationArrangement.Stacked, int output = 1080,
        DisplayMetadata? metadata = null) => engine.Calculate(new(1600, 1200), metadata ?? Metadata,
            new() { ExposureItemGap = gap, Arrangement = arrangement, OutputSize = output });
    private static TextRunLayout[] Exposure(LayoutResult result) => result.TextRuns.Where(run =>
        run.Text is "35mm" or "f/4" or "1/250s" or "ISO 200").ToArray();

    [Theory]
    [InlineData(InformationArrangement.TwoColumns)]
    [InlineData(InformationArrangement.Stacked)]
    public void GapChangesPositionsNotGlyphWidthsAndPreservesOuterAlignment(InformationArrangement arrangement)
    {
        var narrow = Calculate(4, arrangement); var wide = Calculate(30, arrangement);
        Assert.True(narrow.IsValid); Assert.True(wide.IsValid);
        var first = Exposure(narrow); var second = Exposure(wide);
        Assert.Equal(4, first.Length); Assert.Equal(4, second.Length);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(first[index].Text, second[index].Text);
            Assert.Equal(first[index].Bounds.Width, second[index].Bounds.Width, 8);
            if (index > 0) Assert.Equal(30, second[index].Bounds.Left - second[index - 1].Bounds.Right, 8);
        }
        if (arrangement == InformationArrangement.TwoColumns) Assert.Equal(wide.TextSafeBounds.Right, second[^1].Bounds.Right, 8);
        else Assert.Equal(540, (second[0].Bounds.Left + second[^1].Bounds.Right) / 2, 8);
    }

    [Fact]
    public void MissingOrHiddenItemsDoNotLeaveSlotsOrExtraGaps()
    {
        var result = Calculate(31, metadata: Metadata with { Aperture = "", Shutter = "  " });
        var runs = Exposure(result); Assert.Equal(2, runs.Length);
        Assert.Equal(31, runs[1].Bounds.Left - runs[0].Bounds.Right, 8);
        var hidden = engine.Calculate(new(1600, 1200), Metadata, new() { Visibility = new() { Exposure = false } });
        Assert.Empty(Exposure(hidden));
        Assert.Empty(Exposure(Calculate(31, metadata: Metadata with { FocalLength = "", Aperture = "", Shutter = "", Iso = "" })));
    }

    [Fact]
    public void GapAndGeometryScaleTogetherAt2048()
    {
        var small = Calculate(27); var large = Calculate(27, output: 2048); var factor = 2048d / 1080;
        Assert.True(small.IsValid); Assert.True(large.IsValid);
        for (var index = 0; index < small.TextRuns.Count; index++)
        {
            Assert.Equal(small.TextRuns[index].Bounds.Left * factor, large.TextRuns[index].Bounds.Left, 7);
            Assert.Equal(small.TextRuns[index].Bounds.Top * factor, large.TextRuns[index].Bounds.Top, 7);
        }
        Assert.Equal(small.PhotoBounds.Width * factor, large.PhotoBounds.Width, 7);
    }

    [Fact]
    public void LargeGapWrapsWholeItemsWithoutChangingLineSpacingOrIgnoringGap()
    {
        var result = Calculate(200); Assert.True(result.IsValid);
        var rows = Exposure(result).GroupBy(run => run.Bounds.Top).ToArray(); Assert.True(rows.Length > 1);
        foreach (var row in rows)
        {
            var runs = row.ToArray();
            for (var index = 1; index < runs.Length; index++) Assert.Equal(200, runs[index].Bounds.Left - runs[index - 1].Bounds.Right, 8);
        }
        Assert.Empty(LayoutValidator.Validate(result));
        var verbose = Metadata with { FocalLength = new string('1', 18), Aperture = new string('2', 18), Shutter = new string('3', 18), Iso = new string('4', 18) };
        var unsafeResult = engine.Calculate(new(1600, 1200), verbose,
            new() { Arrangement = InformationArrangement.Stacked, ExposureItemGap = 200, PhotoTopMargin = 400, BottomSafeDistance = 540, CropExtraPadding = 200 });
        var fitting = engine.Calculate(new(1600, 1200), verbose,
            new() { Arrangement = InformationArrangement.Stacked, ExposureItemGap = 0, PhotoTopMargin = 400, BottomSafeDistance = 540, CropExtraPadding = 200 });
        Assert.True(fitting.IsValid); Assert.False(unsafeResult.IsValid);
    }
}
