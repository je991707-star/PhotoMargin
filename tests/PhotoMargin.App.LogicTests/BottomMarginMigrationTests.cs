using System.Text.Json;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void Version6RemovesOnlyLegacyDualProtectionAndPreservesAllEightNumericConfigurations() => Ui.Run(async () =>
    {
        var rule = A;
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var index = (int)aspect * 2 + (arrangement == InformationArrangement.Stacked ? 1 : 0);
            rule = rule.WithConfiguration(aspect, arrangement, new()
            { Side = 50 + index, Top = 100 + index, Bottom = index, Gap = 39 + index, Extra = 17 + index,
                ExposureItemGap = 16 + index, ProtectPortrait = true, ProtectLandscape = true });
        }
        var raw = new PersistedSettings
        { Version = 6, MarginRules = [rule, B with { Id = "landscape-only", ProtectPortrait = false, ProtectLandscape = true, Arrangements = null, Configurations = null }],
            SelectedMarginRuleId = rule.Id, Layout = new() { BottomSafeDistance = 0, CropExtraPadding = 111, ProtectFiveByFour = true } };
        var path = Path.Combine(root, "legacy-dual.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var before = File.ReadAllBytes(path); var store = new SettingsStore(path); var loaded = await store.LoadAsync();
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(raw.SelectedMarginRuleId, loaded.SelectedMarginRuleId);
        Assert.Equal(0, loaded.Layout.BottomSafeDistance); Assert.Equal(111, loaded.Layout.CropExtraPadding); Assert.False(loaded.Layout.ProtectFiveByFour);
        var migrated = loaded.MarginRules[0]; Assert.Equal(rule.Id, migrated.Id); Assert.Equal(rule.Name, migrated.Name);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
            Assert.Equal(rule.ForAspect(aspect, arrangement) with { ProtectLandscape = false }, migrated.ForAspect(aspect, arrangement));
        Assert.True(loaded.MarginRules[1].ForAspect(SourceAspect.FourByThree).ProtectLandscape);
        await store.SaveAsync(loaded); Assert.Equal(migrated, (await store.LoadAsync()).MarginRules[0]);
        // Explicit combinations in the new format must never be re-migrated.
        var explicitRule = migrated.WithConfiguration(SourceAspect.FourByThree, InformationArrangement.Stacked,
            migrated.ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked) with { ProtectLandscape = true });
        await store.SaveAsync(loaded with { Layout = loaded.Layout with { ProtectFiveByFour = true }, MarginRules = [explicitRule] });
        var explicitLoaded = await store.LoadAsync(); Assert.True(explicitLoaded.Layout.ProtectFiveByFour);
        Assert.True(explicitLoaded.MarginRules[0].ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked).ProtectLandscape);
        Assert.Equal(PersistedSettings.CurrentVersion, explicitLoaded.Version);
    });

    [Fact]
    public void NewDefaultsAndLegacyMissingFlagsDoNotReintroduceAutomaticLandscapeProtection() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "old-missing.json");
        await File.WriteAllTextAsync(path, """{"version":3,"layout":{"bottomSafeDistance":0,"cropExtraPadding":0},"marginRules":[]}""");
        using var vm = Create(path); await vm.InitializeAsync();
        Assert.False(vm.ProtectFiveByFour); Assert.Equal(0, vm.BottomSafeDistance); Assert.Equal(0, vm.CropExtraPadding);
        Assert.All(vm.MarginRules, rule => Assert.False(rule.ProtectLandscape));
        await vm.FlushSettingsAsync(); using var restarted = Create(path); await restarted.InitializeAsync();
        Assert.False(restarted.ProtectFiveByFour); Assert.Equal(0, restarted.BottomSafeDistance); await restarted.FlushSettingsAsync();
    });
}
