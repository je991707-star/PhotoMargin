namespace PhotoMargin.Core.Models;

public enum SourceOrientation
{
    Landscape,
    Portrait,
    Square
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}

// Select between the existing information layouts, not photo orientation or text alignment.
public enum InformationArrangement
{
    Auto = 0,
    TwoColumns = 1,
    Stacked = 2
}

public sealed record FieldVisibility
{
    public bool Camera { get; init; } = true;
    public bool Lens { get; init; } = true;
    public bool Exposure { get; init; } = true;
    public bool Date { get; init; }
}

public sealed record LayoutSettings
{
    public int OutputSize { get; init; } = 1080;
    public int JpegQuality { get; init; } = 95;
    public double PhotoSideMargin { get; init; } = 48;
    public double PhotoTopMargin { get; init; } = 36;
    public double PhotoInfoGap { get; init; } = 24;
    public double BottomSafeDistance { get; init; } = 72;
    public double CropExtraPadding { get; init; } = 24;
    public double PrimaryFontSize { get; init; } = 28;
    public double SecondaryFontSize { get; init; } = 22;
    public double LensFontSize { get; init; } = 22;
    public double DateFontSize { get; init; } = 22;
    public double MinimumFontSize { get; init; } = 18;
    public bool ProtectFourByFive { get; init; } = true;
    public bool ProtectFiveByFour { get; init; } = true;
    public string FontFamily { get; init; } = "Microsoft JhengHei UI";
    public InformationArrangement Arrangement { get; init; } = InformationArrangement.Auto;
    public FieldVisibility Visibility { get; init; } = new();

    public double Scale => OutputSize / 1080d;

    public LayoutSettings Validate() => this with
    {
        Arrangement = Enum.IsDefined(Arrangement) ? Arrangement : InformationArrangement.Auto,
        OutputSize = Math.Clamp(OutputSize, 320, 8192),
        JpegQuality = Math.Clamp(JpegQuality, 50, 100),
        PhotoSideMargin = Math.Clamp(PhotoSideMargin, 0, 400),
        PhotoTopMargin = Math.Clamp(PhotoTopMargin, 0, 400),
        PhotoInfoGap = Math.Clamp(PhotoInfoGap, 0, 200),
        BottomSafeDistance = Math.Clamp(BottomSafeDistance, 0, 540),
        CropExtraPadding = Math.Clamp(CropExtraPadding, 0, 200),
        PrimaryFontSize = Math.Clamp(PrimaryFontSize, 18, 100),
        SecondaryFontSize = Math.Clamp(SecondaryFontSize, 18, 100),
        LensFontSize = Math.Clamp(double.IsFinite(LensFontSize) ? LensFontSize : 22, 18, 100),
        DateFontSize = Math.Clamp(double.IsFinite(DateFontSize) ? DateFontSize : 22, 18, 100),
        MinimumFontSize = Math.Clamp(MinimumFontSize, 12, 48)
    };
}

public sealed record TextRunLayout(
    string Text,
    RectD Bounds,
    double FontSize,
    string FontFamily,
    string Color,
    TextAlignment Alignment,
    bool IsPrimary);

public sealed record LayoutResult(
    int CanvasSize,
    RectD PhotoBounds,
    RectD TextSafeBounds,
    IReadOnlyList<TextRunLayout> TextRuns,
    bool UsesStackedInfo,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public bool HasInformation => TextRuns.Count > 0;
}

public interface ITextMeasurer
{
    SizeD Measure(string text, string fontFamily, double fontSize);
}
