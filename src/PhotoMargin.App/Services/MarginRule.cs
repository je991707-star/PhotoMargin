using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

// Rules intentionally contain only margins/crop protection, never fonts or export settings.
public sealed record MarginRule
{
    public const string DefaultId = "default";
    public string Id { get; init; } = DefaultId;
    public string Name { get; init; } = "預設規則";
    public double Side { get; init; } = 48;
    public double Top { get; init; } = 36;
    public double Gap { get; init; } = 24;
    public double Bottom { get; init; } = 72;
    public double Extra { get; init; } = 24;
    public bool ProtectPortrait { get; init; } = true;
    public bool ProtectLandscape { get; init; } = true;

    public MarginRule Validate() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "未命名規則" : Name.Trim(),
        Side = Clamp(Side, 400, 48), Top = Clamp(Top, 400, 36),
        Gap = Clamp(Gap, 200, 24), Bottom = Clamp(Bottom, 540, 72), Extra = Clamp(Extra, 200, 24)
    };

    public static MarginRule FromLayout(LayoutSettings layout) => new()
    {
        Side = layout.PhotoSideMargin, Top = layout.PhotoTopMargin, Gap = layout.PhotoInfoGap,
        Bottom = layout.BottomSafeDistance, Extra = layout.CropExtraPadding,
        ProtectPortrait = layout.ProtectFourByFive, ProtectLandscape = layout.ProtectFiveByFour
    };

    private static double Clamp(double value, double maximum, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, 0, maximum);
}
