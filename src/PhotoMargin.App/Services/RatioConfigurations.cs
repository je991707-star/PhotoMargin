using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

// Immutable, value-equal snapshots: no mutable nested dictionary references.
public sealed record MarginConfiguration
{
    public double Side { get; init; } = 48;
    public double Top { get; init; } = 36;
    public double Gap { get; init; } = 24;
    public double ExposureItemGap { get; init; } = 18;
    public double Bottom { get; init; } = 72;
    public double Extra { get; init; } = 24;
    public bool ProtectPortrait { get; init; } = true;
    public bool ProtectLandscape { get; init; }
    public MarginRule ToRule() => new() { Side = Side, Top = Top, Gap = Gap, ExposureItemGap = ExposureItemGap, Bottom = Bottom, Extra = Extra, ProtectPortrait = ProtectPortrait, ProtectLandscape = ProtectLandscape };
    public MarginConfiguration Validate() => From(ToRule().ValidateValues());
    public LayoutSettings ApplyTo(LayoutSettings settings) => settings with
    {
        PhotoSideMargin = Side, PhotoTopMargin = Top, PhotoInfoGap = Gap, ExposureItemGap = ExposureItemGap,
        BottomSafeDistance = Bottom, CropExtraPadding = Extra,
        ProtectFourByFive = ProtectPortrait, ProtectFiveByFour = ProtectLandscape
    };
    public static MarginConfiguration From(MarginRule rule) => new()
    { Side = rule.Side, Top = rule.Top, Gap = rule.Gap, ExposureItemGap = rule.ExposureItemGap, Bottom = rule.Bottom, Extra = rule.Extra, ProtectPortrait = rule.ProtectPortrait, ProtectLandscape = rule.ProtectLandscape };
}

// Rendering identity belongs to the key, never to a copied parameter object.
public sealed record ArrangementConfigurations
{
    public MarginConfiguration? TwoColumns { get; init; }
    public MarginConfiguration? Stacked { get; init; }
    public MarginConfiguration Get(InformationArrangement arrangement) =>
        (arrangement == InformationArrangement.Stacked ? Stacked : TwoColumns)!;
    public ArrangementConfigurations With(InformationArrangement arrangement, MarginConfiguration value) =>
        arrangement == InformationArrangement.Stacked ? this with { Stacked = value.Validate() } : this with { TwoColumns = value.Validate() };
    public ArrangementConfigurations CopyValidated(MarginConfiguration fallback) => new()
    {
        TwoColumns = (TwoColumns ?? Stacked ?? fallback).Validate(),
        Stacked = (Stacked ?? TwoColumns ?? fallback).Validate()
    };
}

public sealed record RatioArrangementConfigurations
{
    public ArrangementConfigurations? SixteenByNine { get; init; }
    public ArrangementConfigurations? FiveByFour { get; init; }
    public ArrangementConfigurations? FourByThree { get; init; }
    public ArrangementConfigurations? ThreeByTwo { get; init; }
    public ArrangementConfigurations? Get(SourceAspect aspect) => aspect switch
    {
        SourceAspect.SixteenByNine => SixteenByNine, SourceAspect.FiveByFour => FiveByFour,
        SourceAspect.ThreeByTwo => ThreeByTwo, _ => FourByThree
    };
    public RatioArrangementConfigurations With(SourceAspect aspect, ArrangementConfigurations value) => aspect switch
    {
        SourceAspect.SixteenByNine => this with { SixteenByNine = value },
        SourceAspect.FiveByFour => this with { FiveByFour = value },
        SourceAspect.ThreeByTwo => this with { ThreeByTwo = value },
        _ => this with { FourByThree = value }
    };
    public RatioArrangementConfigurations CopyValidated(RatioConfigurations legacy) => new()
    {
        SixteenByNine = (SixteenByNine ?? new()).CopyValidated(legacy.SixteenByNine),
        FiveByFour = (FiveByFour ?? new()).CopyValidated(legacy.FiveByFour),
        FourByThree = (FourByThree ?? new()).CopyValidated(legacy.FourByThree),
        ThreeByTwo = (ThreeByTwo ?? new()).CopyValidated(legacy.ThreeByTwo)
    };
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
