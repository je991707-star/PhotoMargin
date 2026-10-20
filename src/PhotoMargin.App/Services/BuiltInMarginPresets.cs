using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using System.Text.Json;

namespace PhotoMargin.App.Services;

// The single source for creating/recreating the built-in preset. Parameter DTO
// defaults remain backward-compatible; they are not the built-in preset table.
public static class BuiltInMarginPresets
{
    public const int CurrentRevision = 2;
    private static readonly Lazy<MarginRule> Resource = new(() =>
    {
        using var stream = typeof(BuiltInMarginPresets).Assembly.GetManifestResourceStream("PhotoMargin.BuiltInMarginPresets.json")
            ?? throw new InvalidDataException("缺少內建預設資源。");
        return (JsonSerializer.Deserialize<MarginRule>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("內建預設資源無效。")).Validate();
    });
    internal static MarginConfiguration LegacyConfiguration => new()
    { Side = 48, Top = 36, Bottom = 72, Gap = 24, ExposureItemGap = 18, Extra = 24, ProtectPortrait = true, ProtectLandscape = false };

    public static MarginRule Create(string name = "預設規則")
    {
        return (Resource.Value with { Name = name }).Validate();
    }

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
