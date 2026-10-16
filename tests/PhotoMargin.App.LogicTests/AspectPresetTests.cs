using System.Text.Json;
using ImageMagick;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    private static async Task DemoReady(MainViewModel vm)
    {
        await Until(() => vm.RuleEditor.CompletedPreviewVersion == vm.RuleEditor.PreviewVersion);
        Assert.Empty(vm.RuleEditor.PreviewError); Assert.NotNull(vm.RuleEditor.PreviewBytes);
    }

    [Fact]
    public void OfflineDemoAndFourCropsRemainSquareWithoutContaminatingWork() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); var work = vm.WorkingMargins;
        Assert.Equal(2, vm.RuleEditor.AspectIndex); vm.RuleEditor.StartPreview(); await DemoReady(vm);
        Assert.NotEmpty(vm.RuleEditor.PreviewLayout!.TextRuns);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        {
            vm.RuleEditor.AspectIndex = (int)aspect; await DemoReady(vm);
            var (w, h) = aspect.Dimensions(); var size = vm.RuleEditor.PreviewSourceSize;
            Assert.Equal((double)w / h, size.Width / size.Height, 10);
            using var image = new MagickImage(vm.RuleEditor.PreviewBytes!);
            Assert.Equal(image.Width, image.Height); Assert.Equal(720u, image.Width);
            Assert.True(vm.RuleEditor.PreviewLayout!.IsValid);
            Assert.Equal(work, vm.WorkingMargins); Assert.Null(vm.SourcePath); Assert.Null(vm.PreviewBytes);
            Assert.Empty(vm.Camera); Assert.Empty(vm.Lens); Assert.Null(vm.SelectedDate);
        }
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void AllFourDraftsSurviveSwitchesSaveAndRestartWithStableId() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        {
            vm.RuleEditor.AspectIndex = (int)aspect;
            vm.RuleEditor.Side = 80 + (int)aspect; vm.RuleEditor.Top = 40 + (int)aspect;
            vm.RuleEditor.Gap = 25 + (int)aspect; vm.RuleEditor.Bottom = 75 + (int)aspect;
            vm.RuleEditor.Extra = 20 + (int)aspect; vm.RuleEditor.ProtectPortrait = (int)aspect % 2 == 0;
            vm.RuleEditor.ProtectLandscape = (int)aspect % 2 != 0;
        }
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        { vm.RuleEditor.AspectIndex = (int)aspect; Assert.Equal(80 + (int)aspect, vm.RuleEditor.Side); Assert.Equal($"{80 + (int)aspect} px", vm.RuleEditor.SideText); }
        Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        Assert.True(await vm.RuleEditor.SaveAsync()); await vm.FlushSettingsAsync();
        using var restarted = Create(PresetPath); await restarted.InitializeAsync(); await restarted.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        var saved = Assert.Single(restarted.MarginRules); Assert.Equal("a", saved.Id);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        {
            restarted.RuleEditor.AspectIndex = (int)aspect;
            var value = saved.ForAspect(aspect);
            Assert.Equal(80 + (int)aspect, restarted.RuleEditor.Side); Assert.Equal(40 + (int)aspect, value.Top);
            Assert.Equal(25 + (int)aspect, value.Gap); Assert.Equal(75 + (int)aspect, value.Bottom);
            Assert.Equal(20 + (int)aspect, value.Extra); Assert.Equal((int)aspect % 2 == 0, value.ProtectPortrait);
            Assert.Equal((int)aspect % 2 != 0, value.ProtectLandscape);
        }
        Assert.False(restarted.RuleEditor.HasDirtyChanges);
        await restarted.FlushSettingsAsync();
    });

    [Fact]
    public void RatioChangesDoNotCommitDraftAndDiscardDropsEveryConfiguration() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); var work = vm.WorkingMargins;
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        foreach (var aspect in Enum.GetValues<SourceAspect>()) { vm.RuleEditor.AspectIndex = (int)aspect; vm.RuleEditor.Side = 140 + (int)aspect; }
        await vm.FlushSettingsAsync(); Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        Assert.False(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(false))); Assert.Equal(143, vm.RuleEditor.Side);
        Assert.True(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(true))); Assert.Equal(work, vm.WorkingMargins);
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        foreach (var aspect in Enum.GetValues<SourceAspect>()) { vm.RuleEditor.AspectIndex = (int)aspect; Assert.Equal(A.Side, vm.RuleEditor.Side); }
        Assert.False(vm.RuleEditor.HasDirtyChanges);
        await vm.FlushSettingsAsync();
    });

    [Fact]
    public void SliderUpdatesActualGeometryBeforeSavingAndLatestRequestWins() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        vm.RuleEditor.StartPreview(); await DemoReady(vm); var before = vm.RuleEditor.PreviewLayout!;
        vm.RuleEditor.Side = 200; await DemoReady(vm);
        Assert.True(vm.RuleEditor.PreviewLayout!.PhotoBounds.Width < before.PhotoBounds.Width);
        var beforeGap = vm.RuleEditor.PreviewLayout;
        vm.RuleEditor.Gap = 80; await DemoReady(vm);
        Assert.NotEqual(beforeGap, vm.RuleEditor.PreviewLayout);
        for (var index = 0; index < 40; index++)
        { vm.RuleEditor.AspectIndex = index % 4; vm.RuleEditor.Side = 90 + index; }
        vm.RuleEditor.AspectIndex = 0; vm.RuleEditor.Side = 177; await DemoReady(vm);
        Assert.Equal(0, vm.RuleEditor.AspectIndex); Assert.Equal(177, vm.RuleEditor.Side);
        Assert.Equal(16d / 9, vm.RuleEditor.PreviewSourceSize.Width / vm.RuleEditor.PreviewSourceSize.Height, 10);
        Assert.Equal(A, vm.MarginRules[0]); Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ContinuousInputPublishesFramesBeforeTheGestureEnds() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        vm.RuleEditor.StartPreview(); await DemoReady(vm); var first = vm.RuleEditor.PreviewBytes;
        var frameDuringGesture = false;
        for (var index = 0; index < 80; index++)
        {
            vm.RuleEditor.Side = 80 + index;
            await Task.Delay(16);
            frameDuringGesture |= !ReferenceEquals(first, vm.RuleEditor.PreviewBytes);
        }
        Assert.True(frameDuringGesture); await DemoReady(vm); Assert.Equal(159, vm.RuleEditor.Side);
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ActualPhotoDemonstrationUsesCopyAndExportRetainsFullOriginalRatio() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); var path = Source(true); var sourceBytes = File.ReadAllBytes(path);
        await vm.LoadPhotoAsync(path); await Until(() => vm.CanExport);
        vm.Camera = "Manual camera"; await Until(() => vm.CanExport); var main = vm.PreviewBytes;
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); vm.RuleEditor.StartPreview();
        foreach (var aspect in Enum.GetValues<SourceAspect>()) { vm.RuleEditor.AspectIndex = (int)aspect; await DemoReady(vm); }
        Assert.Same(main, vm.PreviewBytes); Assert.Equal("Manual camera", vm.Camera); Assert.Equal(path, vm.SourcePath);
        var output = Path.Combine(root, "export.jpg"); await vm.ExportAsync(output);
        using var image = new MagickImage(output); Assert.Equal(1080u, image.Width); Assert.Equal(1080u, image.Height);
        Assert.Equal(sourceBytes, File.ReadAllBytes(path));
        using var loaded = new ImageLoader(new ExifReader()).Load(path); Assert.Equal(1.5, loaded.OrientedSize.Width / loaded.OrientedSize.Height);
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void LegacyV4MigratesOnceIntoIndependentConfigurationsWithoutChangingIdOrValues() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "legacy.json");
        File.WriteAllText(path, """{"version":4,"marginRules":[{"id":"legacy-id","name":"Legacy","side":69,"top":51,"gap":32,"bottom":91,"extra":31,"protectPortrait":false}],"selectedMarginRuleId":"legacy-id"}""");
        var store = new SettingsStore(path); var settings = await store.LoadAsync(); var rule = Assert.Single(settings.MarginRules);
        Assert.Equal("legacy-id", rule.Id); Assert.Equal("Legacy", rule.Name); Assert.Equal(2, (int)settings.DemonstrationAspect);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        { var c = rule.ForAspect(aspect); Assert.Equal(69, c.Side); Assert.Equal(51, c.Top); Assert.Equal(32, c.Gap); Assert.Equal(91, c.Bottom); Assert.Equal(31, c.Extra); Assert.False(c.ProtectPortrait); }
        Assert.NotSame(rule.Configurations!.FourByThree, rule.Configurations.SixteenByNine);
        var changed = rule.WithConfiguration(SourceAspect.SixteenByNine, rule.ForAspect(SourceAspect.SixteenByNine) with { Side = 151 });
        await store.SaveAsync(settings with { MarginRules = [changed] });
        var restored = (await store.LoadAsync()).MarginRules[0];
        Assert.Equal(151, restored.ForAspect(SourceAspect.SixteenByNine).Side); Assert.Equal(69, restored.ForAspect(SourceAspect.FourByThree).Side);
        using var json = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal(5, json.RootElement.GetProperty("version").GetInt32());
    });

    [Fact]
    public void PresetApplicationChoosesOrientedSourceRatioNotDemoSelection() => Ui.Run(async () =>
    {
        var rule = A;
        foreach (var aspect in Enum.GetValues<SourceAspect>()) rule = rule.WithConfiguration(aspect, new() { Side = 100 + (int)aspect });
        using var vm = await Presets(rule); await vm.LoadPhotoAsync(Source());
        vm.RuleEditor.AspectIndex = 0; vm.ApplyPresetToWork(rule);
        Assert.Equal(103, vm.PhotoSideMargin); // actual 900x600 = 3:2, demonstration = 16:9
        Assert.Equal(SourceAspect.SixteenByNine, SourceAspects.Closest(new(4032, 2268)));
        Assert.Equal(SourceAspect.FourByThree, SourceAspects.Closest(new(4655, 3492)));
        Assert.Equal(SourceAspect.FiveByFour, SourceAspects.Closest(new(1251, 1000)));
        Assert.Equal(SourceAspect.ThreeByTwo, SourceAspects.Closest(new(6001, 4000)));
        Assert.Equal(SourceAspect.SixteenByNine, SourceAspects.Closest(new(2000, 1000)));
        var rotatedPath = Path.Combine(root, "rotated.jpg");
        using (var image = new MagickImage(MagickColors.Coral, 600, 900))
        { image.Orientation = OrientationType.RightTop; image.Write(rotatedPath); }
        await vm.LoadPhotoAsync(rotatedPath); vm.ApplyPresetToWork(rule); Assert.Equal(103, vm.PhotoSideMargin);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });
}
