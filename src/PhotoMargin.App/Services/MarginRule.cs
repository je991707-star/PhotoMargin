using PhotoMargin.Core.Models;
using PhotoMargin.Core;

namespace PhotoMargin.App.Services;

// Rules intentionally contain only margins/crop protection, never fonts or export settings.
public sealed record MarginRule
{
    public const string DefaultId = "default";
    public string Id { get; init; } = DefaultId;
    public string Name { get; init; } = "預設規則";
    public int BuiltInRevision { get; init; }
    public double Side { get; init; } = 48;
    public double Top { get; init; } = 36;
    public double Gap { get; init; } = 24;
    public double ExposureItemGap { get; init; } = 18;
    public double Bottom { get; init; } = 72;
    public double Extra { get; init; } = 24;
    public bool ProtectPortrait { get; init; } = true;
    public bool ProtectLandscape { get; init; }

    public RatioConfigurations? Configurations { get; init; }
    public RatioArrangementConfigurations? Arrangements { get; init; }
    public MarginConfiguration ForAspect(SourceAspect aspect, InformationArrangement arrangement = InformationArrangement.Auto) =>
        Arrangements?.Get(aspect)?.Get(arrangement) ?? Configurations?.Get(aspect) ?? MarginConfiguration.From(this);
    // Legacy callers supplied one configuration for both arrangements.
    public MarginRule WithConfiguration(SourceAspect aspect, MarginConfiguration value) =>
        WithConfiguration(aspect, InformationArrangement.TwoColumns, value).WithConfiguration(aspect, InformationArrangement.Stacked, value);
    public MarginRule WithConfiguration(SourceAspect aspect, InformationArrangement arrangement, MarginConfiguration value)
    {
        var normalized = Validate();
        return (normalized with { Arrangements = normalized.Arrangements!.With(aspect,
            normalized.Arrangements.Get(aspect)!.With(arrangement, value)) }).Validate();
    }
    public MarginRule Validate()
    {
        var normalized = ValidateValues();
        var legacy = (Configurations ?? RatioConfigurations.FromLegacy(normalized)).CopyValidated(normalized);
        var arrangements = (Arrangements ?? new()).CopyValidated(legacy);
        // Keep legacy fields as compatibility snapshots only; eight configurations are authoritative.
        var configurations = new RatioConfigurations
        {
            SixteenByNine = arrangements.SixteenByNine!.TwoColumns! with { },
            FiveByFour = arrangements.FiveByFour!.TwoColumns! with { },
            FourByThree = arrangements.FourByThree!.TwoColumns! with { },
            ThreeByTwo = arrangements.ThreeByTwo!.TwoColumns! with { }
        };
        return configurations.FourByThree.ToRule() with { Id = Id, Name = normalized.Name,
            BuiltInRevision = Id == DefaultId ? BuiltInRevision : 0, Configurations = configurations, Arrangements = arrangements };
    }
    internal MarginRule ValidateValues() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "未命名規則" : Name.Trim(),
        Side = Clamp(Side, 400, 48), Top = Clamp(Top, 400, 36),
        Gap = Clamp(Gap, 200, 24), ExposureItemGap = Clamp(ExposureItemGap, 200, 18), Bottom = Clamp(Bottom, 540, 72), Extra = Clamp(Extra, 200, 24)
    };

    public static MarginRule FromLayout(LayoutSettings layout) => new()
    {
        Side = layout.PhotoSideMargin, Top = layout.PhotoTopMargin, Gap = layout.PhotoInfoGap, ExposureItemGap = layout.ExposureItemGap,
        Bottom = layout.BottomSafeDistance, Extra = layout.CropExtraPadding,
        ProtectPortrait = layout.ProtectFourByFive, ProtectLandscape = layout.ProtectFiveByFour
    };

    private static double Clamp(double value, double maximum, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, 0, maximum);
}
