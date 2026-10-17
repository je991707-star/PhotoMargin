using System.Text.Json;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RevisionOneSavedCatalogIsRepairedOnceWithAnIndependentBackup(bool manualWork) => Ui.Run(async () =>
    {
        // Matches the affected installed settings: all eight entries retain 50px
        // bottom margins, but schema 8/revision 1 prevents the old migration.
        var old = new MarginRule { Top = 100, Gap = 50, Bottom = 50, BuiltInRevision = 1 }.Validate();
        var custom = new MarginRule { Id = "existing-custom", Name = "預設規則（更新前備份）", Side = 179 }.Validate();
        var layout = old.ForAspect(SourceAspect.FourByThree).ApplyTo(new() { LensFontSize = 18,
            PrimaryFontSize = 26, Arrangement = InformationArrangement.TwoColumns, JpegQuality = 92 });
        if (manualWork) layout = layout with { BottomSafeDistance = 7 };
        var path = Path.Combine(root, "catalog-v1.json");
        await new SettingsStore(path).SaveAsync(new() { Layout = layout, MarginRules = [old, custom] });
        using var vm = Create(path); await vm.InitializeAsync();
        var current = vm.MarginRules.Single(rule => rule.Id == MarginRule.DefaultId);
        Assert.Equal(BuiltInMarginPresets.Create(), current);
        Assert.Equal(custom, vm.MarginRules.Single(rule => rule.Id == custom.Id));
        var backup = Assert.Single(vm.MarginRules, rule => rule.Id != MarginRule.DefaultId && rule.Id != custom.Id);
        Assert.Equal("預設規則（更新前備份） 2", backup.Name);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            Assert.Equal(old.ForAspect(aspect, arrangement), backup.ForAspect(aspect, arrangement));
            Assert.NotSame(current.ForAspect(aspect, arrangement), backup.ForAspect(aspect, arrangement));
        }
        Assert.Equal(manualWork ? 7 : 112, vm.BottomSafeDistance);
        Assert.Equal(18, vm.LensFontSize); Assert.Equal(26, vm.PrimaryFontSize); Assert.Equal(92, vm.JpegQuality);
        Assert.Equal(MarginRule.DefaultId, vm.SelectedMarginRuleId);
        // Selecting an arrangement applies the repaired catalog, not the backup.
        vm.Arrangement = InformationArrangement.Stacked;
        Assert.Equal(100, vm.BottomSafeDistance); Assert.Equal(32, vm.PhotoInfoGap);
        await vm.FlushSettingsAsync();
        using var restored = Create(path); await restored.InitializeAsync();
        Assert.Equal(3, restored.MarginRules.Count); Assert.Equal(backup, restored.MarginRules.Single(rule => rule.Id == backup.Id));
        Assert.Equal(100, restored.BottomSafeDistance); await restored.FlushSettingsAsync();
    });

    [Fact]
    public void SchemaEightStillUpgradesRevisionZeroButDoesNotResetCurrentRevisionEdits() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "catalog-v0.json"); var store = new SettingsStore(path);
        await store.SaveAsync(new() { MarginRules = [new MarginRule().Validate()] });
        var loaded = await store.LoadAsync();
        Assert.Equal(BuiltInMarginPresets.Create(), Assert.Single(loaded.MarginRules));
        Assert.Equal(112, loaded.Layout.BottomSafeDistance);
        var edited = loaded.MarginRules[0].WithConfiguration(SourceAspect.FourByThree,
            InformationArrangement.Stacked, new() { Bottom = 13 });
        await store.SaveAsync(loaded with { MarginRules = [edited] });
        Assert.Equal(edited, Assert.Single((await store.LoadAsync()).MarginRules));
    });

    [Fact]
    public async Task RevisionOneCanonicalTableOnlyUpdatesMarkerWithoutDuplicatingPresets()
    {
        var path = Path.Combine(root, "catalog-current-values.json"); var store = new SettingsStore(path);
        await store.SaveAsync(new() { MarginRules = [BuiltInMarginPresets.Create() with { BuiltInRevision = 1 }] }, TestContext.Current.CancellationToken);
        Assert.Equal(BuiltInMarginPresets.Create(), Assert.Single((await store.LoadAsync(TestContext.Current.CancellationToken)).MarginRules));
    }

    [Theory]
    [InlineData(0, 0, 40, 100, 170, 50, 18)]
    [InlineData(1, 0, 48, 50, 90, 50, 18)]
    [InlineData(2, 0, 48, 100, 112, 50, 18)]
    [InlineData(3, 0, 48, 100, 136, 50, 18)]
    [InlineData(0, 1, 40, 100, 150, 50, 20)]
    [InlineData(1, 1, 48, 50, 70, 50, 20)]
    [InlineData(2, 1, 48, 100, 100, 32, 20)]
    [InlineData(3, 1, 48, 100, 106, 50, 18)]
    public void BuiltInTableMatchesAllSixValues(int aspect, int arrangement, double side, double top, double bottom, double gap, double exposure)
    {
        var rule = BuiltInMarginPresets.Create();
        var values = rule.ForAspect((SourceAspect)aspect, arrangement == 1 ? InformationArrangement.Stacked : InformationArrangement.TwoColumns);
        Assert.Equal(new MarginConfiguration { Side = side, Top = top, Bottom = bottom, Gap = gap, ExposureItemGap = exposure, Extra = 24 }, values);
        Assert.True(values.ProtectPortrait); Assert.False(values.ProtectLandscape);
        Assert.Equal(MarginRule.DefaultId, rule.Id); Assert.Equal(BuiltInMarginPresets.CurrentRevision, rule.BuiltInRevision);
        Assert.NotSame(values, BuiltInMarginPresets.Create().ForAspect((SourceAspect)aspect,
            arrangement == 1 ? InformationArrangement.Stacked : InformationArrangement.TwoColumns));
    }

    [Fact]
    public void FirstLaunchUsesRealBuiltInTableAndChoosesSourceRatioWithoutOverwritingManualWork() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); var builtin = BuiltInMarginPresets.Create();
        Assert.Equal(builtin, Assert.Single(vm.MarginRules)); Assert.Equal(112, vm.BottomSafeDistance); Assert.Equal(16, vm.LensFontSize);
        await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        Assert.Equal(136, vm.BottomSafeDistance); Assert.Equal(50, vm.PhotoInfoGap);
        vm.LensFontSize = 12; vm.ApplyWorkingMargins(new() { Side = 177, Bottom = 91 });
        await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        Assert.Equal(177, vm.PhotoSideMargin); Assert.Equal(91, vm.BottomSafeDistance); Assert.Equal(12, vm.LensFontSize);
        await vm.FlushSettingsAsync();
    });

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void LegacyBuiltInMigrationOnlyUpdatesUntouchedConfigurationsAndNeverCustomIds(int version) => Ui.Run(async () =>
    {
        var old = new MarginRule { Name = "Retain built-in name", ProtectLandscape = version < 7 }.Validate();
        var edited = old.WithConfiguration(SourceAspect.SixteenByNine, InformationArrangement.Stacked,
            old.ForAspect(SourceAspect.SixteenByNine, InformationArrangement.Stacked) with { Side = 153, Bottom = 29, Extra = 88 });
        var custom = old with { Id = "custom", Name = "Custom stays unchanged" };
        var data = new PersistedSettings { Version = version, MarginRules = [edited, custom], SelectedMarginRuleId = MarginRule.DefaultId,
            Layout = new() { LensFontSize = 12, ProtectFiveByFour = version < 7 } };
        var path = Path.Combine(root, "old-table.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var store = new SettingsStore(path); var loaded = await store.LoadAsync(); var result = loaded.MarginRules.Single(rule => rule.Id == MarginRule.DefaultId);
        Assert.Equal(2, loaded.MarginRules.Count); Assert.Equal(edited.Name, result.Name); Assert.Equal(12, loaded.Layout.LensFontSize);
        var factory = BuiltInMarginPresets.Create();
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var value = result.ForAspect(aspect, arrangement);
            if (aspect == SourceAspect.SixteenByNine && arrangement == InformationArrangement.Stacked)
                Assert.Equal(edited.ForAspect(aspect, arrangement) with { ProtectLandscape = false }, value);
            else Assert.Equal(factory.ForAspect(aspect, arrangement), value);
            Assert.Equal(custom.ForAspect(aspect, arrangement) with { ProtectLandscape = false }, loaded.MarginRules.Single(rule => rule.Id == "custom").ForAspect(aspect, arrangement));
        }
        Assert.Equal(112, loaded.Layout.BottomSafeDistance); // unchanged old work snapshot upgrades alongside its built-in copy
        await store.SaveAsync(loaded); Assert.Equal(result, (await store.LoadAsync()).MarginRules.Single(rule => rule.Id == MarginRule.DefaultId));
        // A later intentional edit back to the old numbers must not be upgraded again.
        var intentional = result.WithConfiguration(SourceAspect.FourByThree, InformationArrangement.Stacked, new());
        await store.SaveAsync(loaded with { MarginRules = [intentional, loaded.MarginRules.Single(rule => rule.Id == "custom")] });
        Assert.Equal(new MarginConfiguration(), (await store.LoadAsync()).MarginRules[0].ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked));
    });

    [Fact]
    public void EditedWorkAndEmptyPresetListAreNotResetAndRecreationUsesFreshTable() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "edited-work.json");
        var data = new PersistedSettings { Version = 7, MarginRules = [new MarginRule().Validate()],
            Layout = new() { PhotoSideMargin = 211, PhotoTopMargin = 123, BottomSafeDistance = 0, LensFontSize = 12 } };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var store = new SettingsStore(path); var loaded = await store.LoadAsync(); Assert.Equal(data.Layout, loaded.Layout);
        var altered = BuiltInMarginPresets.Create().WithConfiguration(SourceAspect.FourByThree, InformationArrangement.Stacked, new() { Side = 211 });
        Assert.NotEqual(altered, BuiltInMarginPresets.Create()); Assert.Equal(100, BuiltInMarginPresets.Create().ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked).Bottom);
        await store.SaveAsync(loaded with { MarginRules = [] }); Assert.Empty((await store.LoadAsync()).MarginRules);
    });

    [Fact]
    public void LensInputSelectionPreferencesAndPresetApplicationKeepSizesBelow18() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync();
        foreach (var size in new[] { 8d, 10, 12, 14, 16, 18, 12.5 })
        {
            Assert.True(vm.TrySetLensFontSizeText(size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(size, vm.LensFontSize); Assert.Contains(size, vm.LensFontSizes);
        }
        Assert.False(vm.TrySetLensFontSizeText("NaN")); Assert.False(vm.TrySetLensFontSizeText("invalid")); Assert.Equal(12.5, vm.LensFontSize);
        vm.LensFontSize = 12; vm.ApplyPresetToWork(BuiltInMarginPresets.Create());
        Assert.Equal(12, vm.LensFontSize); vm.PrimaryFontSize = 8; vm.SecondaryFontSize = 8; vm.DateFontSize = 8;
        Assert.Equal(18, vm.PrimaryFontSize); Assert.Equal(18, vm.SecondaryFontSize); Assert.Equal(18, vm.DateFontSize);
        await vm.FlushSettingsAsync(); using var restored = Create(); await restored.InitializeAsync();
        Assert.Equal(12, restored.LensFontSize); await restored.FlushSettingsAsync();
    });
}
