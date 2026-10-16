using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class PhotoMetadataTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("zh-TW")]
    public void ExifNumericStringsUseConsistentDecimalSyntax(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var metadata = new PhotoMetadata { FocalLengthMm = 35.5, FNumber = 2.8, ExposureSeconds = 1.3 };
            Assert.Equal("35.5mm", metadata.FocalLengthDisplay); Assert.Equal("f/2.8", metadata.ApertureDisplay);
            Assert.Equal("1.3s", metadata.ShutterDisplay);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void InvalidExifNumbersAreNeverRendered()
    {
        var metadata = new PhotoMetadata { FocalLengthMm = double.PositiveInfinity, FNumber = double.NaN, ExposureSeconds = double.PositiveInfinity };
        Assert.Empty(metadata.FocalLengthDisplay); Assert.Empty(metadata.ApertureDisplay); Assert.Empty(metadata.ShutterDisplay);
    }
    [Theory]
    [InlineData(0.004, "1/250s")]
    [InlineData(0.5, "1/2s")]
    [InlineData(0.8, "0.8s")]
    [InlineData(0.6, "0.6s")]
    [InlineData(0.4, "0.4s")]
    [InlineData(2, "2s")]
    public void FormatsShutter(double seconds, string expected)
    {
        Assert.Equal(expected, new PhotoMetadata { ExposureSeconds = seconds }.ShutterDisplay);
    }

    [Fact]
    public void MakeIsNotReplacedByStyleBrand()
    {
        var metadata = new PhotoMetadata { Make = "Panasonic", Model = "DC-GH5" };
        Assert.Equal("Panasonic DC-GH5", metadata.CameraDisplay);
    }

    [Fact]
    public void ReloadOriginalReplacesManualDisplayOverrides()
    {
        var editable = new EditableMetadata();
        var original = new PhotoMetadata { Make = "FUJIFILM", Model = "X-T5", Iso = 400 };
        editable.LoadOriginal(original);
        editable.Camera = "手動內容";
        editable.LoadOriginal(original);

        Assert.Equal("FUJIFILM X-T5", editable.Camera);
        Assert.Equal("ISO 400", editable.Iso);
    }
}
