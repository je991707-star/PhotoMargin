using PhotoMargin.Core.Models;
using PhotoMargin.Core;

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

    public RatioConfigurations? Configurations { get; init; }
    public MarginConfiguration ForAspect(SourceAspect aspect) => Configurations?.Get(aspect) ?? MarginConfiguration.From(this);
    public MarginRule WithConfiguration(SourceAspect aspect, MarginConfiguration value) =>
        (this with { Configurations = (Configurations ?? RatioConfigurations.FromLegacy(this)).With(aspect, value) }).Validate();
    public MarginRule Validate()
    {
        var normalized = ValidateValues();
        var configurations = (Configurations ?? RatioConfigurations.FromLegacy(normalized)).CopyValidated(normalized);
        // Flat fields remain the 4:3 compatibility snapshot; ratio data is authoritative.
        return configurations.FourByThree.ToRule() with { Id = Id, Name = normalized.Name, Configurations = configurations };
    }
    internal MarginRule ValidateValues() => this with
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
