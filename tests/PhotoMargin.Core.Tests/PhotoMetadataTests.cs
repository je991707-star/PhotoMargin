using PhotoMargin.Core.Models;

namespace PhotoMargin.Core.Tests;

public sealed class PhotoMetadataTests
{
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
