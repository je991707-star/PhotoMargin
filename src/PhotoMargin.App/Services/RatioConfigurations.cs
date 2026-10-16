using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

// Immutable, value-equal snapshots: no mutable nested dictionary references.
public sealed record MarginConfiguration
{
    public double Side { get; init; } = 48;
    public double Top { get; init; } = 36;
    public double Gap { get; init; } = 24;
    public double Bottom { get; init; } = 72;
    public double Extra { get; init; } = 24;
    public bool ProtectPortrait { get; init; } = true;
    public bool ProtectLandscape { get; init; } = true;
    public MarginRule ToRule() => new() { Side = Side, Top = Top, Gap = Gap, Bottom = Bottom, Extra = Extra, ProtectPortrait = ProtectPortrait, ProtectLandscape = ProtectLandscape };
    public MarginConfiguration Validate() => From(ToRule().ValidateValues());
    public LayoutSettings ApplyTo(LayoutSettings settings) => settings with
    {
        PhotoSideMargin = Side, PhotoTopMargin = Top, PhotoInfoGap = Gap,
        BottomSafeDistance = Bottom, CropExtraPadding = Extra,
        ProtectFourByFive = ProtectPortrait, ProtectFiveByFour = ProtectLandscape
    };
    public static MarginConfiguration From(MarginRule rule) => new()
    { Side = rule.Side, Top = rule.Top, Gap = rule.Gap, Bottom = rule.Bottom, Extra = rule.Extra, ProtectPortrait = rule.ProtectPortrait, ProtectLandscape = rule.ProtectLandscape };
}

public sealed record RatioConfigurations
{
    public MarginConfiguration SixteenByNine { get; init; } = new();
    public MarginConfiguration FiveByFour { get; init; } = new();
    public MarginConfiguration FourByThree { get; init; } = new();
    public MarginConfiguration ThreeByTwo { get; init; } = new();
    public MarginConfiguration Get(SourceAspect aspect) => aspect switch
    {
        SourceAspect.SixteenByNine => SixteenByNine, SourceAspect.FiveByFour => FiveByFour,
        SourceAspect.ThreeByTwo => ThreeByTwo, _ => FourByThree
    };
    public RatioConfigurations With(SourceAspect aspect, MarginConfiguration value) => aspect switch
    {
        SourceAspect.SixteenByNine => this with { SixteenByNine = value with { } },
        SourceAspect.FiveByFour => this with { FiveByFour = value with { } },
        SourceAspect.ThreeByTwo => this with { ThreeByTwo = value with { } },
        _ => this with { FourByThree = value with { } }
    };
    public static RatioConfigurations FromLegacy(MarginRule rule)
    {
        var value = MarginConfiguration.From(rule).Validate();
        return new() { SixteenByNine = value with { }, FiveByFour = value with { }, FourByThree = value with { }, ThreeByTwo = value with { } };
    }
    public RatioConfigurations CopyValidated(MarginRule fallback) => new()
    {
        SixteenByNine = (SixteenByNine ?? MarginConfiguration.From(fallback)).Validate(),
        FiveByFour = (FiveByFour ?? MarginConfiguration.From(fallback)).Validate(),
        FourByThree = (FourByThree ?? MarginConfiguration.From(fallback)).Validate(),
        ThreeByTwo = (ThreeByTwo ?? MarginConfiguration.From(fallback)).Validate()
    };
}
