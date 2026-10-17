using System.Text.Json;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void EightDraftsSurviveSwitchingSavingAndNewViewModel() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        for (var arrangement = 0; arrangement < 2; arrangement++)
        {
            vm.RuleEditor.AspectIndex = (int)aspect; vm.RuleEditor.ArrangementIndex = arrangement;
            var index = (int)aspect * 2 + arrangement;
            vm.RuleEditor.Top = 50 + index; vm.RuleEditor.Side = 80 + index;
            vm.RuleEditor.Gap = 20 + index; vm.RuleEditor.Bottom = 70 + index;
            vm.RuleEditor.Extra = 25 + index; vm.RuleEditor.ExposureItemGap = 10 + index;
        }
        Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        for (var arrangement = 0; arrangement < 2; arrangement++)
        {
            vm.RuleEditor.AspectIndex = (int)aspect; vm.RuleEditor.ArrangementIndex = arrangement;
            Assert.Equal(50 + (int)aspect * 2 + arrangement, vm.RuleEditor.Top);
            Assert.Equal($"{10 + (int)aspect * 2 + arrangement} px", vm.RuleEditor.ExposureItemGapText);
        }
        Assert.True(await vm.RuleEditor.SaveAsync()); await vm.FlushSettingsAsync();
        using var restarted = Create(PresetPath); await restarted.InitializeAsync();
        Assert.Equal(1, restarted.RuleEditor.ArrangementIndex);
        await restarted.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        var saved = Assert.Single(restarted.MarginRules); Assert.Equal(A.Id, saved.Id);
        var objects = new List<MarginConfiguration>();
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        for (var arrangement = 0; arrangement < 2; arrangement++)
        {
            var type = arrangement == 1 ? InformationArrangement.Stacked : InformationArrangement.TwoColumns;
            restarted.RuleEditor.AspectIndex = (int)aspect; restarted.RuleEditor.ArrangementIndex = arrangement;
            var index = (int)aspect * 2 + arrangement; var value = saved.ForAspect(aspect, type);
            Assert.Equal(50 + index, value.Top); Assert.Equal(80 + index, value.Side);
            Assert.Equal(20 + index, value.Gap); Assert.Equal(70 + index, value.Bottom);
            Assert.Equal(25 + index, value.Extra); Assert.Equal(10 + index, value.ExposureItemGap);
            Assert.Equal(value.ExposureItemGap, restarted.RuleEditor.ExposureItemGap);
            Assert.All(objects, previous => Assert.NotSame(previous, value)); objects.Add(value);
        }
        Assert.False(restarted.RuleEditor.HasDirtyChanges); await restarted.FlushSettingsAsync();
    });

    [Fact]
    public void CancellingDiscardsAllEightDraftsAndApplyCannotModifyOrDeleteSavedPreset() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); var work = vm.WorkingMargins;
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        for (var a = 0; a < 4; a++) for (var b = 0; b < 2; b++)
        { vm.RuleEditor.AspectIndex = a; vm.RuleEditor.ArrangementIndex = b; vm.RuleEditor.ExposureItemGap = 70 + a * 2 + b; }
        Assert.False(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(false)));
        Assert.Equal(77, vm.RuleEditor.ExposureItemGap);
        Assert.True(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(true)));
        Assert.Equal(work, vm.WorkingMargins); Assert.Equal(A, vm.MarginRules[0]);
        vm.RuleEditor.ExposureItemGap = 45; Assert.Equal(45, vm.ExposureItemGap);
        Assert.False(vm.RuleEditor.CanDelete); Assert.Null(vm.RuleEditor.BeginDeletion());
        await vm.FlushSettingsAsync(); Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
    });

    [Fact]
    public void MainApplicationUsesOrientedRatioAndCurrentArrangementAndCopiesValues() => Ui.Run(async () =>
    {
        var rule = A;
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        {
            rule = rule.WithConfiguration(aspect, InformationArrangement.TwoColumns, new() { Top = 60 + (int)aspect, ExposureItemGap = 11 });
            rule = rule.WithConfiguration(aspect, InformationArrangement.Stacked, new() { Top = 90 + (int)aspect, ExposureItemGap = 35 });
        }
        using var vm = await Presets(rule); await vm.LoadPhotoAsync(Source());
        vm.Arrangement = InformationArrangement.Stacked; vm.ApplyPresetToWork(rule);
        Assert.Equal(93, vm.PhotoTopMargin); Assert.Equal(35, vm.ExposureItemGap);
        vm.Arrangement = InformationArrangement.TwoColumns;
        Assert.Equal(63, vm.PhotoTopMargin); Assert.Equal(11, vm.ExposureItemGap);
        vm.ApplyWorkingMargins(new() { Top = 123, ExposureItemGap = 56 });
        Assert.Equal(rule, vm.MarginRules[0]); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void Version5MigratesEveryRatioWithoutChangingIdentityValuesOrSelectedArrangement() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "v5.json");
        await File.WriteAllTextAsync(path, """{"version":5,"layout":{"arrangement":2,"photoTopMargin":97},"marginRules":[{"id":"legacy","name":"Keep name","configurations":{"sixteenByNine":{"top":61},"fiveByFour":{"top":62},"fourByThree":{"top":63},"threeByTwo":{"top":64}}}],"selectedMarginRuleId":"legacy"}""");
        var store = new SettingsStore(path); var loaded = await store.LoadAsync(); var rule = Assert.Single(loaded.MarginRules);
        Assert.Equal("legacy", rule.Id); Assert.Equal("Keep name", rule.Name);
        Assert.Equal(InformationArrangement.Stacked, loaded.Layout.Arrangement);
        Assert.Equal(InformationArrangement.Stacked, loaded.DemonstrationArrangement);
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        {
            var left = rule.ForAspect(aspect, InformationArrangement.TwoColumns); var vertical = rule.ForAspect(aspect, InformationArrangement.Stacked);
            Assert.Equal(61 + (int)aspect, left.Top); Assert.Equal(left, vertical); Assert.NotSame(left, vertical); Assert.Equal(18, left.ExposureItemGap);
        }
        rule = rule.WithConfiguration(SourceAspect.FourByThree, InformationArrangement.Stacked, new() { Top = 177, ExposureItemGap = 39 });
        await store.SaveAsync(loaded with { MarginRules = [rule] }); var again = await store.LoadAsync();
        Assert.Equal(rule, again.MarginRules[0]); Assert.Equal(177, again.MarginRules[0].ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked).Top);
        Assert.Equal(63, again.MarginRules[0].ForAspect(SourceAspect.FourByThree, InformationArrangement.TwoColumns).Top);
        using var json = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal(PersistedSettings.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
    });

    [Fact]
    public void PartialArrangementDataOnlyFillsMissingConfigurationsAndNeverOverwritesExisting()
    {
        var rule = (A with { Arrangements = new()
        {
            FourByThree = new() { Stacked = new() { Top = 199, ExposureItemGap = 47 } },
            ThreeByTwo = new() { Stacked = new() { Top = 111 }, TwoColumns = new() { Top = 72 } }
        } }).Validate();
        Assert.Equal(199, rule.ForAspect(SourceAspect.FourByThree, InformationArrangement.Stacked).Top);
        Assert.Equal(199, rule.ForAspect(SourceAspect.FourByThree, InformationArrangement.TwoColumns).Top);
        Assert.NotSame(rule.Arrangements!.FourByThree!.Stacked, rule.Arrangements.FourByThree.TwoColumns);
        Assert.Equal(111, rule.ForAspect(SourceAspect.ThreeByTwo, InformationArrangement.Stacked).Top);
        Assert.Equal(72, rule.ForAspect(SourceAspect.ThreeByTwo, InformationArrangement.TwoColumns).Top);
        Assert.Equal(A.Top, rule.ForAspect(SourceAspect.SixteenByNine).Top); Assert.Equal(rule, rule.Validate());
    }

    [Fact]
    public void PreviewUsesEachArrangementAndLastRapidSelectionWithoutPollutingMain() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        vm.RuleEditor.StartPreview(); vm.RuleEditor.ArrangementIndex = 0; await DemoReady(vm);
        var left = vm.RuleEditor.PreviewLayout!;
        vm.RuleEditor.ArrangementIndex = 1; await DemoReady(vm);
        Assert.True(vm.RuleEditor.PreviewLayout!.UsesStackedInfo); Assert.NotEqual(left.PhotoBounds, vm.RuleEditor.PreviewLayout.PhotoBounds);
        for (var index = 0; index < 32; index++)
        { vm.RuleEditor.AspectIndex = index % 4; vm.RuleEditor.ArrangementIndex = index % 2; vm.RuleEditor.ExposureItemGap = index; }
        vm.RuleEditor.AspectIndex = 2; vm.RuleEditor.ArrangementIndex = 1; vm.RuleEditor.ExposureItemGap = 43;
        await DemoReady(vm); Assert.True(vm.RuleEditor.PreviewLayout.UsesStackedInfo);
        Assert.Equal(4d / 3, vm.RuleEditor.PreviewSourceSize.Width / vm.RuleEditor.PreviewSourceSize.Height, 8);
        var items = vm.RuleEditor.PreviewLayout.TextRuns.Where(run => run.Text == PhotoMargin.Imaging.LayoutPreviewSource.DemoMetadata.FocalLength ||
            run.Text == PhotoMargin.Imaging.LayoutPreviewSource.DemoMetadata.Aperture).ToArray();
        Assert.Equal(2, items.Length); Assert.Equal(43 * 720d / 1080, items[1].Bounds.Left - items[0].Bounds.Right, 6);
        Assert.Equal(A, vm.MarginRules[0]); Assert.Null(vm.SourcePath); Assert.Empty(vm.Camera);
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });

    [Theory]
    [InlineData(1080)]
    [InlineData(2048)]
    public void ActualExportUsesTheSameMeasuredSpacingAndDoesNotChangeSource(int output) => Ui.Run(async () =>
    {
        var rule = A.WithConfiguration(SourceAspect.ThreeByTwo, InformationArrangement.Stacked,
            new() { Top = 57, ExposureItemGap = 41 });
        using var vm = await Presets(rule); var source = Source(true); var originalBytes = File.ReadAllBytes(source);
        await vm.LoadPhotoAsync(source); vm.Arrangement = InformationArrangement.Stacked; vm.ApplyPresetToWork(rule);
        vm.OutputSize = output; vm.ShowGuides = true; await Until(() => vm.CanExport);
        var metadata = new DisplayMetadata(vm.Camera, vm.Lens, vm.FocalLength, vm.Aperture, vm.Shutter, vm.Iso,
            vm.SelectedDate!.Value.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture) +
            $" {vm.SelectedTime!.Value.Hours:00}:{vm.SelectedTime.Value.Minutes:00}");
        var settings = rule.ForAspect(SourceAspect.ThreeByTwo, InformationArrangement.Stacked).ApplyTo(new()
        { OutputSize = output, Arrangement = InformationArrangement.Stacked, Visibility = new() { Date = true } });
        using var loaded = new PhotoMargin.Imaging.ImageLoader(new PhotoMargin.Imaging.ExifReader()).Load(source);
        var layout = new LayoutEngine(new PhotoMargin.Imaging.MagickTextMeasurer()).Calculate(loaded.OrientedSize, metadata, settings);
        Assert.True(layout.IsValid); Assert.Equal(1.5, layout.PhotoBounds.Width / layout.PhotoBounds.Height, 7);
        var runs = layout.TextRuns.Where(run => run.Text == vm.FocalLength || run.Text == vm.Aperture).ToArray();
        Assert.Equal(41 * output / 1080d, runs[1].Bounds.Left - runs[0].Bounds.Right, 6);
        var measurer = new PhotoMargin.Imaging.MagickTextMeasurer();
        Assert.Equal(runs[0].Bounds.Top + measurer.MeasureBaseline(runs[0].Text, runs[0].FontFamily, runs[0].FontSize),
            runs[1].Bounds.Top + measurer.MeasureBaseline(runs[1].Text, runs[1].FontFamily, runs[1].FontSize), 6);
        var path = Path.Combine(root, $"actual-spacing-{output}.jpg"); await vm.ExportAsync(path);
        using var expected = new ImageMagick.MagickImage(new PhotoMargin.Imaging.ImageRenderer().RenderJpeg(loaded, layout, 95));
        using var actual = new ImageMagick.MagickImage(path);
        Assert.Equal(0, expected.Compare(actual, ImageMagick.ErrorMetric.Absolute));
        Assert.Equal(originalBytes, File.ReadAllBytes(source)); await vm.FlushSettingsAsync();
    });
}
