using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

// The single source for creating/recreating the built-in preset. Parameter DTO
// defaults remain backward-compatible; they are not the built-in preset table.
public static class BuiltInMarginPresets
{
    public const int CurrentRevision = 2;
    internal static MarginConfiguration LegacyConfiguration => new()
    { Side = 48, Top = 36, Bottom = 72, Gap = 24, ExposureItemGap = 18, Extra = 24, ProtectPortrait = true, ProtectLandscape = false };

    public static MarginRule Create(string name = "預設規則")
    {
        var configurations = new RatioArrangementConfigurations
        {
            SixteenByNine = Pair(40, 100, 170, 150, 50, 20),
            FiveByFour = Pair(48, 50, 90, 70, 50, 20),
            FourByThree = Pair(48, 100, 112, 100, 32, 20),
            ThreeByTwo = Pair(48, 100, 136, 106, 50, 18)
        };
        return new MarginRule { Name = name, BuiltInRevision = CurrentRevision, Arrangements = configurations }.Validate();
    }

    private static ArrangementConfigurations Pair(double side, double top, double horizontalBottom,
        double verticalBottom, double verticalGap, double verticalExposureGap) => new()
    {
        TwoColumns = new() { Side = side, Top = top, Bottom = horizontalBottom, Gap = 50, ExposureItemGap = 18, Extra = 24 },
        Stacked = new() { Side = side, Top = top, Bottom = verticalBottom, Gap = verticalGap, ExposureItemGap = verticalExposureGap, Extra = 24 }
    };

    internal static MarginRule UpgradeLegacy(MarginRule source)
    {
        var rule = source.Validate();
        if (rule.Id != MarginRule.DefaultId || rule.BuiltInRevision != 0) return rule;
        var updated = Create(rule.Name);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            // Preserve the ENTIRE configuration if any value/protection was edited.
            if (rule.ForAspect(aspect, arrangement) == LegacyConfiguration)
                rule = rule.WithConfiguration(aspect, arrangement, updated.ForAspect(aspect, arrangement));
        }
        return rule with { BuiltInRevision = CurrentRevision };
    }

    internal static (MarginRule Current, MarginRule? Backup) RefreshPreviousCatalog(
        MarginRule source, IEnumerable<string> existingNames)
    {
        // Revision 1 marked the entire catalog complete even when every old
        // configuration was retained. Repair that release once, without losing
        // those saved edits or changing any existing custom preset's identity.
        if (source.Id != MarginRule.DefaultId || source.BuiltInRevision != 1) return (source, null);
        var current = Create(source.Name);
        var differs = Enum.GetValues<SourceAspect>().Any(aspect =>
            new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked }.Any(arrangement =>
                source.ForAspect(aspect, arrangement) != current.ForAspect(aspect, arrangement)));
        if (!differs) return (current, null);
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = source.Name + "（更新前備份）";
        var name = baseName;
        for (var suffix = 2; names.Contains(name); suffix++) name = baseName + " " + suffix;
        var backup = (source with { Id = Guid.NewGuid().ToString("N"), Name = name, BuiltInRevision = 0 }).Validate();
        return (current, backup);
    }
}
