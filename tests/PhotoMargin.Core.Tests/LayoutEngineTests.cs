using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class LayoutEngineTests
{
    private readonly LayoutEngine engine = new(new PredictableTextMeasurer());

    public static TheoryData<double, double> LandscapeRatios => new()
    {
        { 3, 2 },
        { 4, 3 },
        { 16, 9 },
        { 2, 1 }
    };

    [Theory]
    [MemberData(nameof(LandscapeRatios))]
    public void LandscapePhotoIsContainedWithoutCropping(double width, double height)
    {
        var layout = engine.Calculate(
            new SizeD(width * 1000, height * 1000),
            CompleteMetadata(),
            new LayoutSettings());

        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        Assert.Equal(width / height, layout.PhotoBounds.Width / layout.PhotoBounds.Height, 6);
        Assert.InRange(layout.PhotoBounds.Left, 48 - 0.1, 540);
        Assert.True(layout.TextRuns.All(run => layout.TextSafeBounds.Contains(run.Bounds)));
    }

    [Fact]
    public void NoInformationRecentersPhotoAndRemovesGap()
    {
        var layout = engine.Calculate(
            new SizeD(1500, 1000),
            new DisplayMetadata("", "", "", "", "", "", ""),
            new LayoutSettings());

        Assert.True(layout.IsValid);
        Assert.Empty(layout.TextRuns);
        Assert.Equal(540, (layout.PhotoBounds.Top + layout.PhotoBounds.Bottom) / 2, 6);
    }

    [Fact]
    public void LongLensFallsBackToWrappedStack()
    {
        var metadata = CompleteMetadata() with
        {
            Lens = "M.ZUIKO DIGITAL ED 150-400mm F4.5 TC1.25x IS PRO 非常長的鏡頭名稱與增距鏡組合版本，完整名稱不得被截斷"
        };

        var layout = engine.Calculate(new SizeD(6000, 4000), metadata, new LayoutSettings());
        Assert.True(layout.UsesStackedInfo);
        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        Assert.True(layout.TextRuns.Count > 3);
    }

    [Fact]
    public void CustomOutputMaintainsExactCanvasSize()
    {
        var layout = engine.Calculate(new SizeD(1600, 900), CompleteMetadata(), new LayoutSettings { OutputSize = 1500 });
        Assert.Equal(1500, layout.CanvasSize);
        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
    }

    [Fact]
    public void FourInformationGroupsUseIndependentFontSizes()
    {
        var metadata = new DisplayMetadata("Camera", "Lens", "35mm", "f/4", "1/250s", "ISO 200", "2023.11.08 16:15");
        var layout = engine.Calculate(new SizeD(1800, 1200), metadata, new LayoutSettings
        {
            PrimaryFontSize = 28, LensFontSize = 30, SecondaryFontSize = 22, DateFontSize = 24,
            Visibility = new FieldVisibility { Date = true }
        });
        Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
        Assert.Contains(layout.TextRuns, run => run.Text == "Camera" && run.FontSize == 28);
        Assert.Contains(layout.TextRuns, run => run.Text == "Lens" && run.FontSize == 30);
        Assert.Contains(layout.TextRuns, run => run.Text.Contains("35mm", StringComparison.Ordinal) && run.FontSize == 22);
        Assert.Contains(layout.TextRuns, run => run.Text == "2023.11.08 16:15" && run.FontSize == 24);
    }

    [Theory]
    [InlineData(InformationArrangement.Auto)]
    [InlineData(InformationArrangement.TwoColumns)]
    [InlineData(InformationArrangement.Stacked)]
    public void SupportedArrangementsPreserveContainAndSafety(InformationArrangement arrangement)
    {
        foreach (var (width, height) in new[] { (3d, 2d), (4d, 3d), (16d, 9d), (2d, 1d) })
        foreach (var size in new[] { 1080, 2048 })
        {
            var layout = engine.Calculate(new SizeD(width * 1000, height * 1000), CompleteMetadata(),
                new LayoutSettings { Arrangement = arrangement, OutputSize = size, Visibility = new FieldVisibility { Date = true } });
            Assert.True(layout.IsValid, string.Join("; ", layout.Errors));
            Assert.Equal(arrangement == InformationArrangement.Stacked, layout.UsesStackedInfo);
            Assert.Equal(width / height, layout.PhotoBounds.Width / layout.PhotoBounds.Height, 6);
            Assert.Empty(LayoutValidator.Validate(layout));
            Assert.All(layout.TextRuns, run => Assert.True(layout.TextSafeBounds.Contains(run.Bounds)));
        }
    }

    [Fact]
    public void ForcedStackUsesExistingCenteredStackEvenWhenColumnsFit()
    {
        var columns = engine.Calculate(new SizeD(1800, 1200), CompleteMetadata(), new LayoutSettings());
        var stack = engine.Calculate(new SizeD(1800, 1200), CompleteMetadata(),
            new LayoutSettings { Arrangement = InformationArrangement.Stacked });
        Assert.False(columns.UsesStackedInfo);
        Assert.True(stack.UsesStackedInfo);
        Assert.NotEqual(columns.PhotoBounds, stack.PhotoBounds);
        Assert.All(stack.TextRuns.GroupBy(run => run.Bounds.Top), row =>
            Assert.Equal(540, (row.Min(run => run.Bounds.Left) + row.Max(run => run.Bounds.Right)) / 2, 6));
    }

    [Fact]
    public void ExplicitColumnOverflowSwitchesToValidatedStackWithWarningFlag()
    {
        var metadata = CompleteMetadata() with { Lens = new string('長', 80) };
        var auto = engine.Calculate(new SizeD(1800, 1200), metadata, new LayoutSettings());
        var columns = engine.Calculate(new SizeD(1800, 1200), metadata,
            new LayoutSettings { Arrangement = InformationArrangement.TwoColumns });
        Assert.True(auto.IsValid, string.Join("; ", auto.Errors));
        Assert.True(auto.UsesStackedInfo);
        Assert.True(columns.IsValid, string.Join("; ", columns.Errors));
        Assert.True(columns.UsesStackedInfo); Assert.True(columns.FellBackFromColumns);
        Assert.False(auto.FellBackFromColumns); Assert.Empty(LayoutValidator.Validate(columns));
        Assert.All(columns.TextRuns, run => Assert.Equal(TextAlignment.Center, run.Alignment));
    }

    [Fact]
    public void CenteredOverflowNeverSwitchesBackToColumnsOrDisablesSafety()
    {
        var metadata = CompleteMetadata() with { Lens = new string('長', 5000) };
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var layout = engine.Calculate(new SizeD(4655, 3492), metadata, new LayoutSettings { Arrangement = arrangement });
            Assert.False(layout.IsValid);
            Assert.Equal(arrangement == InformationArrangement.TwoColumns, layout.FellBackFromColumns);
        }
    }

    [Fact]
    public void FittingColumnsAndExplicitCenterDoNotReportColumnFailure()
    {
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var layout = engine.Calculate(new SizeD(4655, 3492), CompleteMetadata(), new LayoutSettings { Arrangement = arrangement });
            Assert.True(layout.IsValid); Assert.False(layout.FellBackFromColumns);
            Assert.Equal(arrangement == InformationArrangement.Stacked, layout.UsesStackedInfo);
        }
    }

    [Fact]
    public void MissingOrUnknownArrangementUsesLegacyAuto()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<LayoutSettings>("{\"OutputSize\":2048}")!;
        Assert.Equal(InformationArrangement.Auto, old.Arrangement);
        Assert.Equal(2048, old.OutputSize);
        Assert.Equal(InformationArrangement.Auto, new LayoutSettings { Arrangement = (InformationArrangement)99 }.Validate().Arrangement);
    }

    private static DisplayMetadata CompleteMetadata() =>
        new("Panasonic DC-GH5", "LEICA DG 12-60/F2.8-4.0", "25mm", "f/5.6", "1/250s", "ISO 200", "2026.10.13");

    private sealed class PredictableTextMeasurer : ITextMeasurer
    {
        public SizeD Measure(string text, string fontFamily, double fontSize) =>
            new(text.Sum(character => character > 127 ? fontSize : fontSize * 0.55), fontSize * 1.15);
    }
}
