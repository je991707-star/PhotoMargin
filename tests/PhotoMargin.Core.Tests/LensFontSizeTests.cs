using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class LensFontSizeTests
{
    private sealed class Measurer : ITextMeasurer
    {
        public SizeD Measure(string text, string family, double size) => new(text.Length * size * 0.5, size);
    }
    [Theory]
    [InlineData(8)] [InlineData(12)] [InlineData(16)] [InlineData(18)]
    public void LensUsesUserSizeWithoutSharedMinimumOrFixedLineHeight(double fontSize)
    {
        foreach (var output in new[] { 1080, 2048 })
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var layout = new LayoutEngine(new Measurer()).Calculate(new(1600, 900), new("", "Long lens name 12-60mm F2.8-4.0", "", "", "", "", ""),
                new() { LensFontSize = fontSize, MinimumFontSize = 48, OutputSize = output, Arrangement = arrangement });
            Assert.True(layout.IsValid, string.Join(";", layout.Errors)); var run = Assert.Single(layout.TextRuns);
            Assert.Equal(fontSize * output / 1080d, run.FontSize, 8); Assert.Equal(run.FontSize, run.Bounds.Height, 8);
        }
    }
    [Fact]
    public void OnlyLensLowerBoundChangesAndDefaultIs16()
    {
        var settings = new LayoutSettings { PrimaryFontSize = 8, SecondaryFontSize = 8, DateFontSize = 8, LensFontSize = 2 }.Validate();
        Assert.Equal(8, settings.LensFontSize); Assert.Equal(18, settings.PrimaryFontSize); Assert.Equal(18, settings.SecondaryFontSize); Assert.Equal(18, settings.DateFontSize);
        Assert.Equal(16, new LayoutSettings().LensFontSize); Assert.Equal(16, new LayoutSettings { LensFontSize = double.NaN }.Validate().LensFontSize);
        Assert.Equal(100, new LayoutSettings { LensFontSize = 200 }.Validate().LensFontSize);
    }
}
